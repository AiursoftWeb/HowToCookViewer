using System.Text.Json;
using Aiursoft.Scanner.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace Aiursoft.HowToCookViewer.Services.FileStorage;

/// <summary>
/// Represents a service for storing and managing files. (Level 3: Business Gateway)
/// </summary>
public class StorageService(
    FeatureFoldersProvider folders,
    FileLockProvider fileLockProvider,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration? configuration = null) : ITransientDependency
{
    private const string ProtectorPurpose = "FileOperation/v2";

    public async Task<string> Save(
        string logicalPath,
        IFormFile file,
        bool isVault = false,
        CancellationToken cancellationToken = default)
    {
        return await SaveStream(
            logicalPath,
            (destination, token) => file.CopyToAsync(destination, token),
            isVault,
            cancellationToken);
    }

    public async Task<string> SaveFromStream(
        string logicalPath,
        Stream stream,
        bool isVault = false,
        CancellationToken cancellationToken = default)
    {
        return await SaveStream(
            logicalPath,
            (destination, token) => stream.CopyToAsync(destination, token),
            isVault,
            cancellationToken);
    }

    public async Task<string> SaveFileFromPhysicalPath(
        string sourcePhysicalPath,
        string destinationLogicalPath,
        bool isVault = false,
        CancellationToken cancellationToken = default)
    {
        await using var source = new FileStream(
            sourcePhysicalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        return await SaveFromStream(destinationLogicalPath, source, isVault, cancellationToken);
    }

    public string GetFilePhysicalPath(string logicalPath, bool isVault = false)
    {
        var root = isVault ? folders.GetVaultFolder() : folders.GetWorkspaceFolder();
        return ResolvePhysicalPath(root, logicalPath);
    }

    public string GetVaultSubfolderFilePhysicalPath(string logicalPath, string subfolder)
    {
        var normalizedLogicalPath = NormalizeLogicalPath(logicalPath);
        var normalizedSubfolder = NormalizeLogicalPath(subfolder);
        var subfolderRoot = ResolvePhysicalPath(folders.GetVaultFolder(), normalizedSubfolder);
        var relativePath = Path.GetRelativePath(normalizedSubfolder, normalizedLogicalPath);
        return ResolvePhysicalPath(subfolderRoot, relativePath);
    }

    public string GetToken(
        string path,
        FilePermission permission,
        bool isVault = false,
        FileUploadPolicy? uploadPolicy = null)
    {
        var normalizedPath = NormalizeLogicalPath(path);
        if (permission == FilePermission.Upload)
        {
            uploadPolicy ??= FileUploadPolicy.Create(FileUploadPolicy.DefaultMaxSizeInMb, allowedExtensions: null);
            if (!uploadPolicy.IsStructurallyValid())
            {
                throw new ArgumentException("The upload policy is invalid.", nameof(uploadPolicy));
            }
        }
        else if (uploadPolicy is not null)
        {
            throw new ArgumentException("Download grants cannot contain an upload policy.", nameof(uploadPolicy));
        }

        var grant = new FileOperationGrant(
            Version: FileOperationGrant.CurrentVersion,
            Path: normalizedPath,
            Permission: permission,
            StorageArea: isVault ? FileStorageArea.Vault : FileStorageArea.Workspace,
            AllowDescendants: permission == FilePermission.Upload,
            UploadPolicy: uploadPolicy);

        var protector = dataProtectionProvider
            .CreateProtector(ProtectorPurpose)
            .ToTimeLimitedDataProtector();
        return protector.Protect(JsonSerializer.Serialize(grant), TimeSpan.FromMinutes(60));
    }

    public bool ValidateToken(
        string requestPath,
        string tokenString,
        FilePermission requiredPermission,
        bool isVault = false)
    {
        return TryValidateToken(requestPath, tokenString, requiredPermission, isVault, out _);
    }

    public bool TryValidateToken(
        string requestPath,
        string tokenString,
        FilePermission requiredPermission,
        bool isVault,
        out FileOperationGrant grant)
    {
        grant = null!;
        if (string.IsNullOrWhiteSpace(tokenString))
        {
            return false;
        }

        try
        {
            var normalizedRequestPath = NormalizeLogicalPath(requestPath);
            var protector = dataProtectionProvider
                .CreateProtector(ProtectorPurpose)
                .ToTimeLimitedDataProtector();
            var tokenData = protector.Unprotect(tokenString);
            var parsedGrant = JsonSerializer.Deserialize<FileOperationGrant>(tokenData);

            if (parsedGrant is null ||
                parsedGrant.Version != FileOperationGrant.CurrentVersion ||
                parsedGrant.Permission != requiredPermission ||
                parsedGrant.StorageArea != (isVault ? FileStorageArea.Vault : FileStorageArea.Workspace) ||
                (requiredPermission == FilePermission.Upload &&
                 (parsedGrant.UploadPolicy is null || !parsedGrant.UploadPolicy.IsStructurallyValid())) ||
                (requiredPermission == FilePermission.Download && parsedGrant.UploadPolicy is not null))
            {
                return false;
            }

            var normalizedAuthorizedPath = NormalizeLogicalPath(parsedGrant.Path);
            var pathMatches = string.Equals(
                                  normalizedRequestPath,
                                  normalizedAuthorizedPath,
                                  StringComparison.Ordinal) ||
                              (parsedGrant.AllowDescendants && normalizedRequestPath.StartsWith(
                                  normalizedAuthorizedPath + "/",
                                  StringComparison.Ordinal));

            if (!pathMatches)
            {
                return false;
            }

            grant = parsedGrant with { Path = normalizedAuthorizedPath };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public string RelativePathToInternetUrl(string relativePath, HttpContext context, bool isVault = false)
    {
        var origin = GetPublicOrigin(configuration)?.GetLeftPart(UriPartial.Authority) ??
                     $"{context.Request.Scheme}://{context.Request.Host}";
        return BuildDownloadUrlOrEmpty(relativePath, isVault, origin);
    }

    public string RelativePathToInternetUrl(string relativePath, bool isVault = false)
    {
        var origin = GetPublicOrigin(configuration)?.GetLeftPart(UriPartial.Authority) ?? string.Empty;
        return BuildDownloadUrlOrEmpty(relativePath, isVault, origin);
    }

    public string GetUploadUrl(
        string subfolder,
        bool isVault = false,
        int maxSizeInMb = FileUploadPolicy.DefaultMaxSizeInMb,
        string? allowedExtensions = null)
    {
        var policy = FileUploadPolicy.Create(
            maxSizeInMb,
            allowedExtensions,
            configuration?.GetValue<bool>("Storage:UploadPolicy:RequireAuthenticatedUser") ?? false,
            configuration?.GetValue<bool>("Storage:UploadPolicy:ReplaceSpacesWithHyphens") ?? false);
        var token = Uri.EscapeDataString(GetToken(subfolder, FilePermission.Upload, isVault, policy));
        var route = isVault ? "upload-private" : "upload";
        return $"/{route}/{RelativePathToUriPath(subfolder)}?token={token}";
    }

    internal static Uri? GetPublicOrigin(IConfiguration? configuration)
    {
        var value = configuration?["Storage:PublicOrigin"];
        if (Uri.TryCreate(value, UriKind.Absolute, out var origin) &&
            origin.Scheme is "http" or "https" &&
            string.IsNullOrEmpty(origin.PathAndQuery.Trim('/')))
        {
            return origin;
        }

        return null;
    }

    private string BuildDownloadUrl(string relativePath, bool isVault, string origin)
    {
        var uriPath = RelativePathToUriPath(relativePath);
        if (!isVault)
        {
            return $"{origin}/download/{uriPath}";
        }

        var token = Uri.EscapeDataString(GetToken(relativePath, FilePermission.Download, isVault: true));
        return $"{origin}/download-private/{uriPath}?token={token}";
    }

    private string BuildDownloadUrlOrEmpty(string relativePath, bool isVault, string origin)
    {
        try
        {
            return BuildDownloadUrl(relativePath, isVault, origin);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private async Task<string> SaveStream(
        string logicalPath,
        Func<Stream, CancellationToken, Task> copy,
        bool isVault,
        CancellationToken cancellationToken)
    {
        var (root, physicalPath) = await ReserveSavePath(logicalPath, isVault, cancellationToken);
        try
        {
            await using var destination = new FileStream(
                physicalPath,
                FileMode.Truncate,
                FileAccess.Write,
                FileShare.None);
            await copy(destination, cancellationToken);
        }
        catch
        {
            File.Delete(physicalPath);
            throw;
        }

        return Path.GetRelativePath(root, physicalPath).Replace("\\", "/");
    }

    private async Task<(string Root, string PhysicalPath)> ReserveSavePath(
        string logicalPath,
        bool isVault,
        CancellationToken cancellationToken)
    {
        var root = isVault ? folders.GetVaultFolder() : folders.GetWorkspaceFolder();
        var physicalPath = ResolvePhysicalPath(root, logicalPath);
        var directory = Path.GetDirectoryName(physicalPath)!;
        Directory.CreateDirectory(directory);

        var fileLock = fileLockProvider.GetLock(directory);
        await fileLock.WaitAsync(cancellationToken);
        try
        {
            while (File.Exists(physicalPath))
            {
                physicalPath = Path.Combine(directory, "_" + Path.GetFileName(physicalPath));
            }

            File.Create(physicalPath).Dispose();
        }
        finally
        {
            fileLock.Release();
        }

        return (root, physicalPath);
    }

    private static string ResolvePhysicalPath(string root, string logicalPath)
    {
        var normalizedLogicalPath = NormalizeLogicalPath(logicalPath);
        var normalizedRoot = Path.GetFullPath(root);
        var physicalPath = Path.GetFullPath(Path.Combine(normalizedRoot, normalizedLogicalPath));
        var relativePath = Path.GetRelativePath(normalizedRoot, physicalPath);

        if (relativePath == ".." ||
            relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
            Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("Restricted path access!", nameof(logicalPath));
        }

        return physicalPath;
    }

    private static string NormalizeLogicalPath(string logicalPath)
    {
        if (string.IsNullOrWhiteSpace(logicalPath) || Path.IsPathRooted(logicalPath))
        {
            throw new ArgumentException("A non-empty relative path is required.", nameof(logicalPath));
        }

        var slashNormalizedPath = logicalPath.Replace('\\', '/');
        if (slashNormalizedPath.StartsWith('/'))
        {
            throw new ArgumentException("An absolute path is not allowed.", nameof(logicalPath));
        }

        var segments = slashNormalizedPath.Split('/');
        if (segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment is "." or ".." ||
                segment.Any(char.IsControl)))
        {
            throw new ArgumentException("The path contains an invalid segment.", nameof(logicalPath));
        }

        return string.Join('/', segments);
    }

    private static string RelativePathToUriPath(string relativePath)
    {
        return string.Join('/', NormalizeLogicalPath(relativePath).Split('/').Select(Uri.EscapeDataString));
    }
}
