using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.HowToCookViewer.Services.FileStorage;

public class FeatureFoldersProvider(StorageRootPathProvider rootPathProvider) : ISingletonDependency
{
    private string BasePath => rootPathProvider.GetStorageRootPath();

    public string GetWorkspaceFolder() => EnsureExists(Path.Combine(BasePath, "Workspace"));

    public string GetVaultFolder() => EnsureExists(Path.Combine(BasePath, "Vault"));

    public string GetClearExifFolder() => EnsureExists(Path.Combine(BasePath, "ClearExif"));

    public string GetCompressedFolder() => EnsureExists(Path.Combine(BasePath, "Compressed"));

    private string EnsureExists(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        return path;
    }
}
