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
        await NavigateAsync("/Manage/Index");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Account/Login\\?", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        var form = Page.Locator("form").Filter(new() { Has = Page.Locator("#EmailOrUserName") });
        await form.Locator("#EmailOrUserName").FillAsync("admin@default.com");
        await form.Locator("#Password").FillAsync("Admin@123456!");
        await form.Locator("button[type=submit]").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Manage/Index(?:\\?.*)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        await NavigateAsync("/Manage/Index");
        await Expect(Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/Manage/Index$", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        await Expect(Page.Locator("h1.h3")).ToBeVisibleAsync();
    }

    private async Task NavigateAsync(string path)
    {
        var response = await Page.GotoAsync(path);
        Assert.IsNotNull(response);
        Assert.IsTrue(response.Ok, $"{path}: HTTP {response.Status}");
    }
}
