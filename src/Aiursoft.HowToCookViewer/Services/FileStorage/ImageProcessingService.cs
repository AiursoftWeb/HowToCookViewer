using Aiursoft.Scanner.Abstractions;
using SkiaSharp;

namespace Aiursoft.HowToCookViewer.Services.FileStorage;

public class ImageProcessingService(
    FeatureFoldersProvider folders,
    StorageService storageService,
    ILogger<ImageProcessingService> logger,
    FileLockProvider fileLockProvider) : ITransientDependency
{
    public Task<bool> IsValidImageAsync(string imagePath)
    {
        try
        {
            using var codec = SKCodec.Create(imagePath);
            if (codec != null)
            {
                logger.LogTrace("File with path {ImagePath} is a valid image", imagePath);
                return Task.FromResult(true);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "File with path {ImagePath} is not a valid image", imagePath);
            return Task.FromResult(false);
        }

        logger.LogWarning("File with path {ImagePath} is not a valid image", imagePath);
        return Task.FromResult(false);
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
            using var codec = SKCodec.Create(sourceAbsolute);
            if (codec == null)
            {
                logger.LogWarning("Not a known image format; skipping EXIF clear for {Source}", sourceAbsolute);
                return sourceAbsolute;
            }

            var origin = codec.EncodedOrigin;
            using var bitmap = SKBitmap.Decode(sourceAbsolute);
            if (bitmap == null)
            {
                logger.LogWarning("Failed to decode image; returning original path for {Source}", sourceAbsolute);
                return sourceAbsolute;
            }

            using var oriented = AutoOrient(bitmap, origin);
            logger.LogInformation("Clearing EXIF: {Source} -> {Target}", sourceAbsolute, targetAbsolute);
            SaveBitmap(oriented, targetAbsolute);
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
            using var codec = SKCodec.Create(sourceAbsolute);
            if (codec == null)
            {
                logger.LogWarning("Not a known image format; skipping compression for {Source}", sourceAbsolute);
                return sourceAbsolute;
            }

            var origin = codec.EncodedOrigin;
            using var bitmap = SKBitmap.Decode(sourceAbsolute);
            if (bitmap == null)
            {
                logger.LogWarning("Failed to decode image; returning original path for {Source}", sourceAbsolute);
                return sourceAbsolute;
            }

            using var oriented = AutoOrient(bitmap, origin);
            using var resized = ResizeBitmap(oriented, width, height);
            logger.LogInformation("Compressing image {Source} -> {Target} (width={Width}, height={Height})",
                sourceAbsolute, targetAbsolute, width, height);
            SaveBitmap(resized, targetAbsolute);
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

    private static SKBitmap AutoOrient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin == SKEncodedOrigin.TopLeft)
            return source;

        float[] matrixValues;
        int outW, outH;

        switch (origin)
        {
            case SKEncodedOrigin.TopRight:
                outW = source.Width;
                outH = source.Height;
                matrixValues = [-1, 0, source.Width - 1, 0, 1, 0, 0, 0, 1];
                break;
            case SKEncodedOrigin.BottomRight:
                outW = source.Width;
                outH = source.Height;
                matrixValues = [-1, 0, source.Width - 1, 0, -1, source.Height - 1, 0, 0, 1];
                break;
            case SKEncodedOrigin.BottomLeft:
                outW = source.Width;
                outH = source.Height;
                matrixValues = [1, 0, 0, 0, -1, source.Height - 1, 0, 0, 1];
                break;
            case SKEncodedOrigin.LeftTop:
                outW = source.Height;
                outH = source.Width;
                matrixValues = [0, 1, 0, 1, 0, 0, 0, 0, 1];
                break;
            case SKEncodedOrigin.RightTop:
                outW = source.Height;
                outH = source.Width;
                matrixValues = [0, -1, source.Height - 1, 1, 0, 0, 0, 0, 1];
                break;
            case SKEncodedOrigin.RightBottom:
                outW = source.Height;
                outH = source.Width;
                matrixValues = [0, -1, source.Height - 1, -1, 0, source.Width - 1, 0, 0, 1];
                break;
            case SKEncodedOrigin.LeftBottom:
                outW = source.Height;
                outH = source.Width;
                matrixValues = [0, 1, 0, -1, 0, source.Width - 1, 0, 0, 1];
                break;
            default:
                return source;
        }

        var result = new SKBitmap(outW, outH);
        using var canvas = new SKCanvas(result);
        var matrix = new SKMatrix { Values = matrixValues };
        canvas.SetMatrix(matrix);
        canvas.DrawImage(SKImage.FromBitmap(source), 0, 0, SKSamplingOptions.Default);
        canvas.Flush();
        return result;
    }

    private static SKBitmap ResizeBitmap(SKBitmap source, int targetWidth, int targetHeight)
    {
        if (targetWidth <= 0 && targetHeight <= 0)
            return source;

        int finalWidth, finalHeight;

        if (targetWidth <= 0)
        {
            var ratio = (float)targetHeight / source.Height;
            finalWidth = Math.Max(1, (int)(source.Width * ratio));
            finalHeight = targetHeight;
        }
        else if (targetHeight <= 0)
        {
            var ratio = (float)targetWidth / source.Width;
            finalWidth = targetWidth;
            finalHeight = Math.Max(1, (int)(source.Height * ratio));
        }
        else
        {
            finalWidth = targetWidth;
            finalHeight = targetHeight;
        }

        var result = new SKBitmap(finalWidth, finalHeight);
        using var canvas = new SKCanvas(result);
        var paint = new SKPaint { IsAntialias = true };
        using (paint)
        {
canvas.DrawImage(SKImage.FromBitmap(source),
                        new SKRect(0, 0, source.Width, source.Height),
                        new SKRect(0, 0, finalWidth, finalHeight),
                        SKSamplingOptions.Default,
                        paint);
        }
        canvas.Flush();
        return result;
    }

    private static void SaveBitmap(SKBitmap bitmap, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var format = ext switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".png" => SKEncodedImageFormat.Png,
            ".gif" => SKEncodedImageFormat.Gif,
            ".webp" => SKEncodedImageFormat.Webp,
            ".bmp" => SKEncodedImageFormat.Bmp,
            _ => SKEncodedImageFormat.Png
        };

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, format == SKEncodedImageFormat.Jpeg ? 90 : 100)
            ?? image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.OpenWrite(path);
        data.SaveTo(stream);
    }
}
