using Aiursoft.HowToCookViewer.Models.ManageViewModels;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class ThemeControllerTests : TestBase
{
    [TestMethod]
    public async Task TestSwitchTheme()
    {
        var model = new SwitchThemeViewModel { Theme = "dark" };
        var response = await Http.PostAsJsonAsync("/api/switch-theme", model);
        response.EnsureSuccessStatusCode();

        // Verify cookie
        var cookies = response.Headers.GetValues("Set-Cookie");
        Assert.IsTrue(cookies.Any(c => c.Contains("prefer-dark=True")));
    }
}
