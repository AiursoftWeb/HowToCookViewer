using Aiursoft.Scanner.Abstractions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Aiursoft.HowToCookViewer.Services.FileStorage;

public class ImageProcessingService(
    FeatureFoldersProvider folders,
    StorageService storageService,
    ILogger<ImageProcessingService> logger,
    FileLockProvider fileLockProvider) : ITransientDependency
{
    public async Task<bool> IsValidImageAsync(string imagePath)
    {
        try
        {
            _ = await Image.DetectFormatAsync(imagePath);
            logger.LogTrace("File with path {ImagePath} is a valid image", imagePath);
            return true;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "File with path {ImagePath} is not a valid image", imagePath);
            return false;
        }
    }

    /// <summary>
    /// Clears the EXIF data while retaining the same resolution,
    /// then writes the result to the "ClearExif" subdirectory.
    /// </summary>
    public async Task<string> ClearExifAsync(string logicalPath, bool isVault = false)
    {
        // 1. Resolve source path (Workspace)
        var sourceAbsolute = storageService.GetFilePhysicalPath(logicalPath, isVault);
        
        // 2. Resolve target path (ClearExif/logicalPath)
        var folderName = isVault ? "Vault" : "Workspace";
        var targetAbsolute = Path.GetFullPath(Path.Combine(folders.GetClearExifFolder(), folderName, logicalPath));

        // 3. Check cache
        if (File.Exists(targetAbsolute) && FileCanBeRead(targetAbsolute))
        {
            logger.LogInformation("EXIF-cleared file already exists: {Target}", targetAbsolute);
            return targetAbsolute;
        }

        // 4. Ensure target directory exists
        var targetDir = Path.GetDirectoryName(targetAbsolute);
        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir!);

        // 5. Process
        var lockOnCreatedFile = fileLockProvider.GetLock(targetAbsolute);
        await lockOnCreatedFile.WaitAsync();
        try
        {
            // Double check inside lock
             if (File.Exists(targetAbsolute) && FileCanBeRead(targetAbsolute))
            {
                return targetAbsolute;
            }

            await WaitTillFileCanBeReadAsync(sourceAbsolute);
            using var image = await Image.LoadAsync(sourceAbsolute);
            image.Mutate(ctx => { ctx.AutoOrient(); });
            image.Metadata.ExifProfile = null;
            logger.LogInformation("Clearing EXIF: {Source} -> {Target}", sourceAbsolute, targetAbsolute);
            await image.SaveAsync(targetAbsolute);
        }
        catch (UnknownImageFormatException e)
        {
            logger.LogWarning(e, "Not a known image format; skipping EXIF clear for {Source}", sourceAbsolute);
            return sourceAbsolute; // Return original if fail
        }
        catch (ImageFormatException e)
        {
            logger.LogWarning(e, "Invalid image; returning original path for {Source}", sourceAbsolute);
            return sourceAbsolute;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to clear EXIF for {Source}", sourceAbsolute);
            return sourceAbsolute;
        }
        finally
        {
            lockOnCreatedFile.Release();
        }

        return targetAbsolute;
    }

    /// <summary>
    /// Compresses the image to the specified width/height.
    /// Also clears EXIF data.
    /// </summary>
    public async Task<string> CompressAsync(string logicalPath, int width, int height, bool isVault = false)
    {
        var sourceAbsolute = storageService.GetFilePhysicalPath(logicalPath, isVault);
        
        // Calculate target path in Compressed folder
        var compressedRoot = folders.GetCompressedFolder();
        var folderName = isVault ? "Vault" : "Workspace";
        var dimensionSuffix = BuildDimensionSuffix(width, height);
        
        // "avatar/2026/01/14/logo.png" -> "avatar/2026/01/14/" + "logo_w100.png"
        var extension = Path.GetExtension(logicalPath);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(logicalPath);
        var directoryInStore = Path.GetDirectoryName(logicalPath) ?? string.Empty;
        
        var newFileName = $"{fileNameWithoutExt}{dimensionSuffix}{extension}";
        var targetAbsolute = Path.GetFullPath(Path.Combine(compressedRoot, folderName, directoryInStore, newFileName));

        if (File.Exists(targetAbsolute) && FileCanBeRead(targetAbsolute))
        {
            logger.LogInformation("Compressed file already exists: {Target}", targetAbsolute);
            return targetAbsolute;
        }
        
        var targetDir = Path.GetDirectoryName(targetAbsolute);
        if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir!);

        var lockOnCreatedFile = fileLockProvider.GetLock(targetAbsolute);
        await lockOnCreatedFile.WaitAsync();
        try
        {
            if (File.Exists(targetAbsolute) && FileCanBeRead(targetAbsolute)) return targetAbsolute;

            await WaitTillFileCanBeReadAsync(sourceAbsolute);
            using var image = await Image.LoadAsync(sourceAbsolute);
            image.Mutate(x => x.AutoOrient());
            image.Metadata.ExifProfile = null;
            image.Mutate(x => x.Resize(width, height));
            logger.LogInformation("Compressing image {Source} -> {Target} (width={Width}, height={Height})",
                sourceAbsolute, targetAbsolute, width, height);
            await image.SaveAsync(targetAbsolute);
        }
        catch (UnknownImageFormatException e)
        {
            logger.LogWarning(e, "Not a known image format; skipping compression for {Source}", sourceAbsolute);
            return sourceAbsolute;
        }
        catch (ImageFormatException e)
        {
             logger.LogWarning(e, "Invalid image; returning original path for {Source}", sourceAbsolute);
             return sourceAbsolute;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to compress {Source}", sourceAbsolute);
            return sourceAbsolute;
        }
        finally
        {
            lockOnCreatedFile.Release();
        }

        return targetAbsolute;
    }

    private static string BuildDimensionSuffix(int width, int height)
    {
        if (width > 0 && height > 0) return $"_w{width}_h{height}";
        if (width > 0) return $"_w{width}";
        if (height > 0) return $"_h{height}";
        return string.Empty;
    }

    private async Task WaitTillFileCanBeReadAsync(string path)
    {
        // Don't wait forever
        int retries = 0;
        while (!FileCanBeRead(path) && retries < 20)
        {
            await Task.Delay(100);
            retries++;
        }
    }

    private bool FileCanBeRead(string path)
    {
        try
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
