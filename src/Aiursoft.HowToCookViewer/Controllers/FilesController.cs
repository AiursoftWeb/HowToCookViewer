using Aiursoft.CSTools.Models;
using Aiursoft.CSTools.Tools;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.HowToCookViewer.Controllers;

/// <summary>
/// This controller is used to handle file operations like upload and download.
/// </summary>
[LimitPerMin]
public class FilesController(
    ImageProcessingService imageCompressor,
    FileDeliveryPolicy deliveryPolicy,
    ILogger<FilesController> logger,
    StorageService storage) : ControllerBase
{
    private const long MultipartOverheadAllowance = 64 * 1024;

    [HttpPost]
    [Route("upload/{**subfolder}")]
    public async Task<IActionResult> Upload(
        [FromRoute] string subfolder,
        [FromQuery] string token)
    {
        return await AuthorizeAndProcessUpload(subfolder, token, isVault: false);
    }

    [HttpPost]
    [Route("upload-private/{**subfolder}")]
    public async Task<IActionResult> UploadPrivate(
        [FromRoute] string subfolder,
        [FromQuery] string token)
    {
        return await AuthorizeAndProcessUpload(subfolder, token, isVault: true);
    }

    private async Task<IActionResult> AuthorizeAndProcessUpload(
        string subfolder,
        string token,
        bool isVault)
    {
        if (!storage.TryValidateToken(
                subfolder,
                token,
                FilePermission.Upload,
                isVault,
                out var grant))
        {
            return Unauthorized("Invalid or expired token.");
        }

        var uploadPolicy = grant.UploadPolicy!;
        if (uploadPolicy.RequireAuthenticatedUser && User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized("Anonymous uploads are not allowed.");
        }

        return await ProcessUpload(subfolder, isVault, uploadPolicy);
    }

    private async Task<IActionResult> ProcessUpload(
        string subfolder,
        bool isVault,
        FileUploadPolicy uploadPolicy)
    {
        var maxRequestBytes = checked(uploadPolicy.MaxBytes + MultipartOverheadAllowance);

        if (Request.ContentLength > maxRequestBytes)
        {
            return PayloadTooLarge();
        }

        var bodySizeFeature = HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = maxRequestBytes;
        }

        IFormCollection form;
        try
        {
            form = await Request.ReadFormAsync(
                new FormOptions
                {
                    MultipartBodyLengthLimit = maxRequestBytes,
                    MultipartHeadersLengthLimit = 16 * 1024,
                    ValueLengthLimit = 16 * 1024
                },
                HttpContext.RequestAborted);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            logger.LogWarning(exception, "Rejected an oversized or malformed file upload.");
            return PayloadTooLarge();
        }

        if (form.Files.Count != 1)
        {
            return BadRequest("Exactly one file must be uploaded.");
        }

        var file = form.Files[0];
        if (file.Length <= 0 || file.Length > uploadPolicy.MaxBytes)
        {
            return PayloadTooLarge();
        }

        var fileName = GetSafeFileName(file.FileName, uploadPolicy.ReplaceSpacesWithHyphens);
        if (fileName is null)
        {
            return BadRequest("Invalid file name.");
        }

        if (!uploadPolicy.IsExtensionAllowed(fileName))
        {
            return BadRequest("The file extension is not allowed by this upload grant.");
        }

        if (uploadPolicy.ValidationKind == FileValidationKind.RasterImage &&
            !await imageCompressor.IsValidImageAsync(file))
        {
            return BadRequest("The uploaded file is not a valid raster image.");
        }

        var storePath = Path.Combine(subfolder, fileName);
        var relativePath = await storage.Save(storePath, file, isVault, HttpContext.RequestAborted);
        return Ok(new
        {
            Path = relativePath,
            InternetPath = storage.RelativePathToInternetUrl(relativePath, HttpContext, isVault)
        });
    }

    [Route("download/{**folderNames}")]
    public Task<IActionResult> Download([FromRoute] string folderNames)
    {
        return ProcessDownload(folderNames, isVault: false);
    }

    [Route("download-private/{**folderNames}")]
    public async Task<IActionResult> DownloadPrivate(
        [FromRoute] string folderNames,
        [FromQuery] string token)
    {
        if (!storage.ValidateToken(
                folderNames,
                token,
                requiredPermission: FilePermission.Download,
                isVault: true))
        {
            return Unauthorized("Invalid or expired token.");
        }

        return await ProcessDownload(folderNames, isVault: true);
    }

    private async Task<IActionResult> ProcessDownload(string folderNames, bool isVault)
    {
        string physicalPath;
        try
        {
            physicalPath = storage.GetFilePhysicalPath(folderNames, isVault);
        }
        catch (ArgumentException)
        {
            return BadRequest("Attempted to access a restricted path.");
        }

        if (!System.IO.File.Exists(physicalPath))
        {
            return NotFound();
        }

        if (!deliveryPolicy.CanRenderInline(Request))
        {
            return this.WebFile(physicalPath, isPrivate: isVault);
        }

        if (physicalPath.IsStaticImage() && await imageCompressor.IsValidImageAsync(physicalPath))
        {
            return await FileWithImageCompressor(folderNames, isVault, physicalPath);
        }

        if (deliveryPolicy.TryGetVerifiedInlineMediaType(folderNames, physicalPath, out var mediaType))
        {
            return this.VerifiedInlineFile(physicalPath, mediaType, isPrivate: isVault);
        }

        return this.WebFile(physicalPath, isPrivate: isVault);
    }

    private async Task<IActionResult> FileWithImageCompressor(
        string logicalPath,
        bool isVault,
        string originalPhysicalPath)
    {
        var passedWidth = int.TryParse(Request.Query["w"], out var width);
        var passedSquare = bool.TryParse(Request.Query["square"], out var square);
        string processedPath;
        if (width > 0 && passedWidth)
        {
            width = SizeCalculator.Ceiling(width);
            processedPath = square && passedSquare
                ? await imageCompressor.CompressAsync(logicalPath, width, width, isVault)
                : await imageCompressor.CompressAsync(logicalPath, width, 0, isVault);
        }
        else
        {
            processedPath = await imageCompressor.ClearExifAsync(logicalPath, isVault);
        }

        if (Path.GetFullPath(processedPath).Equals(Path.GetFullPath(originalPhysicalPath), StringComparison.Ordinal))
        {
            logger.LogWarning("Image sanitization failed for {LogicalPath}; serving it as an attachment.", logicalPath);
            return this.WebFile(originalPhysicalPath, isPrivate: isVault);
        }

        var extension = Path.GetExtension(processedPath).TrimStart('.');
        return this.VerifiedInlineFile(
            processedPath,
            Mime.GetContentType(extension),
            isPrivate: isVault);
    }

    private IActionResult PayloadTooLarge()
    {
        return StatusCode(StatusCodes.Status413PayloadTooLarge, "The uploaded file is too large.");
    }

    private static string? GetSafeFileName(string untrustedFileName, bool replaceSpacesWithHyphens)
    {
        var normalizedName = untrustedFileName.Replace('\\', '/');
        var fileName = Path.GetFileName(normalizedName);
        if (!string.Equals(normalizedName, fileName, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(fileName) ||
            fileName is "." or ".." ||
            fileName.Any(char.IsControl) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return null;
        }

        return replaceSpacesWithHyphens ? fileName.Replace(' ', '-') : fileName;
    }
}
