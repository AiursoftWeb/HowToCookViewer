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
    public async Task SetCultureNonLocalReturn()
    {
        var url = "/Culture/Set?culture=en&returnUrl=https://google.com";
        var response = await Http.GetAsync(url);

        // Assert
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.AreEqual("/", response.Headers.Location?.OriginalString);
    }
}
