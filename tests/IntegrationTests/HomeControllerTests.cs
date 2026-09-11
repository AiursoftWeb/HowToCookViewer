using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Services;

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
    public async Task GetIndexShowsVultrReferralWhenEnabled()
    {
        const string referralUrl = "https://www.vultr.com/?ref=9692114-9J";
        var settingsService = GetService<GlobalSettingsService>();
        await settingsService.UpdateSettingAsync(SettingsMap.ShowVultrPromotion, "True");

        try
        {
            var response = await Http.GetAsync("/");
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync();

            Assert.Contains("HowToCook infrastructure is hosted on Vultr.", html, StringComparison.Ordinal);
            Assert.Contains("Get $300 in Vultr credits", html, StringComparison.Ordinal);
            Assert.Contains("data-bs-target=\"#vultr-referral-modal\"", html, StringComparison.Ordinal);
            Assert.Contains("id=\"vultr-referral-modal\"", html, StringComparison.Ordinal);
            Assert.Contains($"href=\"{referralUrl}\"", html, StringComparison.Ordinal);
            Assert.Contains("This is a referral link.", html, StringComparison.Ordinal);
            Assert.Contains("Go to Vultr", html, StringComparison.Ordinal);
            Assert.Contains("Cancel", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Voxihost", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("voxihost.pl", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await settingsService.UpdateSettingAsync(SettingsMap.ShowVultrPromotion, "False");
        }
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
