using Aiursoft.CSTools.Models;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.HowToCookViewer.Services;

public static class Extensions
{
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

    public static IActionResult WebFile(this ControllerBase controller, string path)
    {
        var (etag, length) = GetFileHttpProperties(path);

        // Handle etag
        controller.Response.Headers.Append("ETag", etag);
        if (controller.Request.Headers.Keys.Contains("If-None-Match"))
        {
            if (controller.Request.Headers["If-None-Match"].ToString().Trim('\"') == etag)
            {
                return new StatusCodeResult(304);
            }
        }

        var fileName = Path.GetFileName(path);
        var asciiFileName = Uri.EscapeDataString(fileName); // Ensuring ASCII encoding

        controller.Response.Headers.Append("Content-Disposition", $"inline; filename*=UTF-8''{asciiFileName}");
        controller.Response.Headers.Append("Content-Length", length.ToString());
        controller.Response.Headers.Append("Cache-Control", $"public, max-age={TimeSpan.FromDays(7).TotalSeconds}");

        var extension = Path.GetExtension(path).TrimStart('.');
        return controller.PhysicalFile(path, Mime.GetContentType(extension), true);
    }
}
