using Aiursoft.CSTools.Attributes;
using Aiursoft.CSTools.Tools;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Aiursoft.HowToCookViewer.Controllers;

/// <summary>
/// This controller is used to handle file operations like upload and download.
/// </summary>
[LimitPerMin]
public class FilesController(
    ImageProcessingService imageCompressor,
    ILogger<FilesController> logger,
    StorageService storage,
    IStringLocalizer<FilesController> localizer) : ControllerBase
{
    [HttpPost]
    [Route("upload/{**subfolder}")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> Upload(
        [FromRoute] string subfolder,
        [FromQuery] string token)
    {
        if (!storage.ValidateToken(subfolder, token, FilePermission.Upload))
        {
            return Unauthorized(localizer["Invalid or expired token."]);
        }
        return await ProcessUpload(subfolder, isVault: false);
    }

    [HttpPost]
    [Route("upload-private/{**subfolder}")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = long.MaxValue, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> UploadPrivate(
        [FromRoute] string subfolder,
        [FromQuery] string token)
    {
        if (!storage.ValidateToken(subfolder, token, FilePermission.Upload))
        {
            return Unauthorized(localizer["Invalid or expired token."]);
        }
        return await ProcessUpload(subfolder, isVault: true);
    }

    private async Task<IActionResult> ProcessUpload(string subfolder, bool isVault)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest();
        }

        // Executing here will let the browser upload the file.
        try
        {
            _ = HttpContext.Request.Form.Files.FirstOrDefault()?.ContentType;
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(e.Message);
        }

        if (HttpContext.Request.Form.Files.Count < 1)
        {
            return BadRequest(localizer["No file uploaded!"]);
        }

        var file = HttpContext.Request.Form.Files.First();
        if (!new ValidFolderName().IsValid(file.FileName))
        {
            return BadRequest(localizer["Invalid file name!"]);
        }

        var storePath = Path.Combine(
            subfolder,
            file.FileName);

        // Save returns the logical path (e.g. avatar/2026/01/14/logo.png)
        var relativePath = await storage.Save(storePath, file, isVault);
        return Ok(new
        {
            Path = relativePath,
            InternetPath = storage.RelativePathToInternetUrl(relativePath, HttpContext, isVault)
        });
    }

    [Route("download/{**folderNames}")]
    public async Task<IActionResult> Download([FromRoute] string folderNames)
    {
        return await ProcessDownload(folderNames, isVault: false);
    }

    [Route("download-private/{**folderNames}")]
    public async Task<IActionResult> DownloadPrivate([FromRoute] string folderNames, [FromQuery] string token)
    {
        if (!storage.ValidateToken(folderNames, token, requiredPermission: FilePermission.Download))
        {
            return Unauthorized(localizer["Invalid or expired token."]);
        }
        return await ProcessDownload(folderNames, isVault: true);
    }

    private async Task<IActionResult> ProcessDownload(string folderNames, bool isVault)
    {
        logger.LogInformation("File download requested for path: {FolderNames} (Vault: {IsVault})", folderNames, isVault);

        if (!ModelState.IsValid)
        {
            return BadRequest();
        }

        // 1. Check if resource exists in Workspace/Vault (using logical path to resolve)
        string physicalPath;
        try
        {
            physicalPath = storage.GetFilePhysicalPath(folderNames, isVault);
        }
        catch (ArgumentException)
        {
            return BadRequest(localizer["Attempted to access a restricted path."]);
        }

        if (!System.IO.File.Exists(physicalPath))
        {
            return NotFound();
        }

        // 2. Image Processing (using logical path)
        // If it is an image, we enforce privacy protection (ClearExif) or resizing
        if (physicalPath.IsStaticImage() && await imageCompressor.IsValidImageAsync(physicalPath))
        {
            logger.LogInformation("Processing image compression/clearing request for logical path: {Path}", folderNames);
            return await FileWithImageCompressor(folderNames, isVault);
        }

        // 3. Standard File Download (Non-image)
        logger.LogInformation("Processing raw file download request for path: {Path}", physicalPath);
        return this.WebFile(physicalPath);
    }

    private async Task<IActionResult> FileWithImageCompressor(string logicalPath, bool isVault)
    {
        var passedWidth = int.TryParse(Request.Query["w"], out var width);
        var passedSquare = bool.TryParse(Request.Query["square"], out var square);
        if (width > 0 && passedWidth)
        {
            width = SizeCalculator.Ceiling(width);
            logger.LogInformation("Compressing image '{Path}' to width: {Width}", logicalPath, width);
            if (square && passedSquare)
            {
                var compressedPath = await imageCompressor.CompressAsync(logicalPath, width, width, isVault);
                logger.LogInformation("Image compressed to square format: {CompressedPath}", compressedPath);
                return this.WebFile(compressedPath);
            }
            else
            {
                var compressedPath = await imageCompressor.CompressAsync(logicalPath, width, 0, isVault);
                logger.LogInformation("Image compressed to rectangular format: {CompressedPath}", compressedPath);
                return this.WebFile(compressedPath);
            }
        }
        else
        {
            logger.LogInformation("No valid width parameter provided for {Path}, width={Width}, passedWidth={PassedWidth}",
                logicalPath, width, passedWidth);
        }

        // If no width or invalid, just clear EXIF (Privacy by Default)
        logger.LogInformation("Clearing EXIF data for image: {Path}", logicalPath);
        var clearedPath = await imageCompressor.ClearExifAsync(logicalPath, isVault);
        return this.WebFile(clearedPath);
    }
}
