using System.Net;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class CultureControllerTests : TestBase
{
    [TestMethod]
    public async Task SetCulture()
    {
        var url = "/Culture/Set?culture=en&returnUrl=/";
        var response = await Http.GetAsync(url);

        // Assert
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [TestMethod]
    public async Task SetCultureEmpty()
    {
        var url = "/Culture/Set?culture=&returnUrl=/";
        var response = await Http.GetAsync(url);

        // Assert
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    [DataRow("zh-CN&returnUrl=/Tips/Detail/8")]
    [DataRow("not/a/culture")]
    public async Task SetCultureInvalid(string culture)
    {
        var response = await Http.GetAsync($"/Culture/Set?culture={Uri.EscapeDataString(culture)}&returnUrl=/");

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.IsFalse(response.Headers.Contains("Set-Cookie"));
    }

    [TestMethod]
    public async Task SetCultureNonLocalReturn()
    {
        var url = "/Culture/Set?culture=en&returnUrl=https://google.com";
        var response = await Http.GetAsync(url);

        // Assert
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.AreEqual("/", response.Headers.Location?.OriginalString);
    }
}
