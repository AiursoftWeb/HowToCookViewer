using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

namespace Aiursoft.HowToCookViewer.Services;

public static class Extensions
{
    private const string RestrictedContentSecurityPolicy =
        "sandbox; default-src 'none'; style-src 'unsafe-inline'; img-src data:";
    private static readonly FileExtensionContentTypeProvider ContentTypeProvider = new();

    public static ViewResult SimpleView(this Controller controller, UiStackLayoutViewModel model)
    {
        var services = controller.HttpContext.RequestServices;
        var injector = services.GetRequiredService<ViewModelArgsInjector>();
        injector.InjectSimple(controller.HttpContext, model);
        return controller.View(model);
    }

    public static ViewResult SimpleView(
        this Controller controller,
        UiStackLayoutViewModel model,
        string viewName)
    {
        var services = controller.HttpContext.RequestServices;
        var injector = services.GetRequiredService<ViewModelArgsInjector>();
        injector.InjectSimple(controller.HttpContext, model);
        return controller.View(viewName, model);
    }

    public static ViewResult StackView(this Controller controller, UiStackLayoutViewModel model)
    {
        var services = controller.HttpContext.RequestServices;
        var injector = services.GetRequiredService<ViewModelArgsInjector>();
        injector.Inject(controller.HttpContext, model);
        return controller.View(model);
    }

    public static ViewResult StackView(
        this Controller controller,
        UiStackLayoutViewModel model,
        string viewName)
    {
        var services = controller.HttpContext.RequestServices;
        var injector = services.GetRequiredService<ViewModelArgsInjector>();
        injector.Inject(controller.HttpContext, model);
        return controller.View(viewName, model);
    }

    private static (string etag, long length) GetFileHttpProperties(string path)
    {
        var fileInfo = new FileInfo(path);
        var etagHash = fileInfo.LastWriteTime.ToUniversalTime().ToFileTime() ^ fileInfo.Length;
        var etag = Convert.ToString(etagHash, 16);
        return (etag, fileInfo.Length);
    }

    public static IActionResult WebFile(
        this ControllerBase controller,
        string path,
        bool isPrivate = false)
    {
        return ServeFile(controller, path, "attachment", "application/octet-stream", isPrivate);
    }

    public static IActionResult VerifiedInlineFile(
        this ControllerBase controller,
        string path,
        string verifiedContentType,
        bool isPrivate = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedContentType);
        return ServeFile(controller, path, "inline", verifiedContentType, isPrivate);
    }

    public static IActionResult IsolatedOriginInlineFile(
        this ControllerBase controller,
        string path,
        bool isPrivate = false)
    {
        var contentType = ContentTypeProvider.TryGetContentType(path, out var value)
            ? value
            : "application/octet-stream";
        return ServeFile(controller, path, "inline", contentType, isPrivate, contentSecurityPolicy: null);
    }

    private static IActionResult ServeFile(
        ControllerBase controller,
        string path,
        string disposition,
        string contentType,
        bool isPrivate,
        string? contentSecurityPolicy = RestrictedContentSecurityPolicy)
    {
        var (etag, length) = GetFileHttpProperties(path);
        controller.Response.Headers["ETag"] = etag;
        if (controller.Request.Headers.IfNoneMatch.Any(value => value?.Trim('"') == etag))
        {
            return new StatusCodeResult(StatusCodes.Status304NotModified);
        }

        var fileName = Path.GetFileName(path);
        var encodedFileName = Uri.EscapeDataString(fileName);
        controller.Response.Headers["Content-Disposition"] =
            $"{disposition}; filename*=UTF-8''{encodedFileName}";
        controller.Response.Headers["Content-Length"] = length.ToString();
        controller.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (contentSecurityPolicy is not null)
        {
            controller.Response.Headers["Content-Security-Policy"] = contentSecurityPolicy;
        }
        controller.Response.Headers["Cache-Control"] = isPrivate
            ? "private, no-store"
            : $"public, max-age={TimeSpan.FromDays(7).TotalSeconds}";

        return controller.PhysicalFile(path, contentType, enableRangeProcessing: true);
    }
}
