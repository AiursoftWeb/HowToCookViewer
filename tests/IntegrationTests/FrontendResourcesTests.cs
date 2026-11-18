namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class FrontendResourcesTests : TestBase
{
    [TestMethod]
    [DataRow("css/app.css", "text/css")]
    [DataRow("js/app.js", "text/javascript")]
    [DataRow("fonts/poppins-latin-400-normal.woff2", "font/woff2")]
    [DataRow("img/flags/cn.png", "image/png")]
    public async Task ServesApplicationUiStackAssets(string path, string contentType)
    {
        var response = await Http.GetAsync($"/node_modules/@aiursoft/uistack/dist/{path}");
        response.EnsureSuccessStatusCode();
        Assert.AreEqual(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.IsGreaterThan(0, (await response.Content.ReadAsByteArrayAsync()).Length);
    }
}
