namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class HomeControllerTests : TestBase
{
    [TestMethod]
    public async Task GetIndex()
    {
        var url = "/";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task GetSelfHost()
    {
        var url = "/Home/SelfHost";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        Assert.IsTrue(content.Contains("Deploy"));
        Assert.IsTrue(content.Contains("Anywhere"));
    }
}
