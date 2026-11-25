using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SkiaSharp;
using static Microsoft.Playwright.Assertions;

namespace Aiursoft.HowToCookViewer.Tests.BrowserTests;

[TestClass]
public class SmokeTests : BrowserTestBase
{
    [TestMethod]
    public async Task LoginPageRendersWithLocalAssets()
    {
        const string assetRoot = "/node_modules/@aiursoft/uistack/dist/";
        var css = Page.WaitForResponseAsync(response => new Uri(response.Url).AbsolutePath == assetRoot + "css/app.css");
        var js = Page.WaitForResponseAsync(response => new Uri(response.Url).AbsolutePath == assetRoot + "js/app.js");
        await NavigateAsync("/Account/Login");
        await Expect(Page.Locator("#EmailOrUserName")).ToBeVisibleAsync();
        await Expect(Page.Locator("#Password")).ToBeVisibleAsync();
        foreach (var response in await Task.WhenAll(css, js))
        {
            Assert.IsTrue(response.Ok, $"{response.Url}: HTTP {response.Status}");
        }
    }

    [TestMethod]
    public async Task LocalLoginGrantsAccessToProtectedPage()
    {
        await SignInThroughBrowserAsync();
        await NavigateAsync("/Manage/Index");
        await Expect(Page).ToHaveURLAsync(new Regex("/Manage/Index$", RegexOptions.IgnoreCase));
        await Expect(Page.Locator("h1.h3")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task AvatarCanBeUploadedAndReplacedThroughTheBrowser()
    {
        await SignInThroughBrowserAsync();
        var changeAvatar = Page.Locator("a[href='/Manage/ChangeAvatar']");
        await changeAvatar.ClickAsync();
        await UploadAndSaveAvatarAsync("first-avatar.png", SKColors.Blue);
        await changeAvatar.ClickAsync();
        await ExpectCurrentAvatarAsync("first-avatar.png");

        await UploadAndSaveAvatarAsync("replacement-avatar.png", SKColors.Red);
        await changeAvatar.ClickAsync();
        await ExpectCurrentAvatarAsync("replacement-avatar.png");
    }

    private async Task SignInThroughBrowserAsync()
    {
        await NavigateAsync("/Manage/Index");
        await Expect(Page).ToHaveURLAsync(new Regex("/Account/Login\\?", RegexOptions.IgnoreCase));

        var form = Page.Locator("form").Filter(new() { Has = Page.Locator("#EmailOrUserName") });
        await form.Locator("#EmailOrUserName").FillAsync("admin@default.com");
        await form.Locator("#Password").FillAsync("Admin@123456!");
        await form.Locator("button[type=submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Manage/Index(?:\\?.*)?$", RegexOptions.IgnoreCase));
    }

    private async Task UploadAndSaveAvatarAsync(string name, SKColor color)
    {
        using var bitmap = new SKBitmap(32, 32);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        var avatarPath = $"avatar/{name}";
        var form = Page.Locator("form[action='/Manage/ChangeAvatar']");
        var uploader = form.Locator(".dropify-wrapper");
        await Expect(uploader).ToBeVisibleAsync();

        var upload = await Page.RunAndWaitForResponseAsync(
            () => uploader.Locator("input[type=file]").SetInputFilesAsync(new FilePayload
            {
                Name = name,
                MimeType = "image/png",
                Buffer = png.ToArray()
            }),
            response => response.Request.Method == "POST" && new Uri(response.Url).AbsolutePath == "/upload/avatar");
        Assert.IsTrue(upload.Ok, $"Avatar upload returned HTTP {upload.Status}.");
        await Expect(uploader.Locator(".dropify-render img[src^='data:image/png']")).ToBeVisibleAsync();
        await Expect(form.Locator("input[name=AvatarUrl]")).ToHaveValueAsync(avatarPath);

        await form.Locator("button[type=submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/Manage(?:/Index)?\\?Message=ChangeAvatarSuccess$", RegexOptions.IgnoreCase));
        await Expect(Page.Locator(".alert-success[role=alert]")).ToBeVisibleAsync();
    }

    private async Task ExpectCurrentAvatarAsync(string name)
    {
        var avatar = Page.Locator($"img.rounded-circle[src$='/download/avatar/{name}?w=256&square=true']");
        await Expect(avatar).ToBeVisibleAsync();
        await Expect(avatar).ToHaveJSPropertyAsync("complete", true);
        Assert.IsTrue(await avatar.EvaluateAsync<int>("img => img.naturalWidth") > 0,
            $"The saved avatar {name} did not load as an image.");
    }

    private async Task NavigateAsync(string path)
    {
        var response = await Page.GotoAsync(path);
        Assert.IsNotNull(response);
        Assert.IsTrue(response.Ok, $"{path}: HTTP {response.Status}");
    }
}
