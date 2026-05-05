namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class ErrorControllerTests : TestBase
{
    [TestMethod]
    [DataRow("/Error/Code500")]
    [DataRow("/Error/Code403?returnUrl=/dashboard")]
    [DataRow("/Error/Code400")]
    [DataRow("/Error/Code401")]
    [DataRow("/Error/Code404")]
    [DataRow("/Error/Code999")]
    public async Task GetError(string url)
    {
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }
}
