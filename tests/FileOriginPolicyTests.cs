using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class FileOriginPolicyTests
{
    [TestMethod]
    public void ArbitraryContentRenderingIsDisabledByDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:PublicOrigin"] = "https://files.example.com",
                ["Storage:RequireDedicatedInlineOrigin"] = "true"
            })
            .Build();
        var policy = new FileDeliveryPolicy(configuration);
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("files.example.com");

        Assert.IsTrue(policy.CanRenderInline(context.Request));
        Assert.IsFalse(policy.CanRenderArbitraryContentInline(context.Request));
    }

    [TestMethod]
    public void InlineRenderingRequiresTheConfiguredFileOrigin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:PublicOrigin"] = "https://files.example.com",
                ["Storage:RequireDedicatedInlineOrigin"] = "true",
                ["Storage:AllowArbitraryInlineOnDedicatedOrigin"] = "true"
            })
            .Build();
        var policy = new FileDeliveryPolicy(configuration);
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("app.example.com");

        Assert.IsFalse(policy.CanRenderInline(context.Request));
        Assert.IsFalse(policy.CanRenderArbitraryContentInline(context.Request));

        context.Request.Host = new HostString("files.example.com");
        Assert.IsTrue(policy.CanRenderInline(context.Request));
        Assert.IsTrue(policy.CanRenderArbitraryContentInline(context.Request));
    }

    [TestMethod]
    public void IsolatedOriginInlineFileUsesTheMappedContentTypeWithoutRestrictiveCsp()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        File.WriteAllText(path, "<script>alert(document.domain)</script>");

        try
        {
            var context = new DefaultHttpContext();
            var controller = new TestController
            {
                ControllerContext = new ControllerContext { HttpContext = context }
            };

            var result = controller.IsolatedOriginInlineFile(path);

            var file = result as PhysicalFileResult;
            Assert.IsNotNull(file);
            Assert.AreEqual("text/html", file.ContentType);
            Assert.StartsWith("inline", context.Response.Headers.ContentDisposition.Single());
            Assert.AreEqual("nosniff", context.Response.Headers.XContentTypeOptions.Single());
            Assert.IsFalse(context.Response.Headers.ContainsKey("Content-Security-Policy"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class TestController : ControllerBase;
}
