using System.Net;
using Aiursoft.DbTools;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Services;
using Microsoft.EntityFrameworkCore;
using Aiursoft.CSTools.Tools;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class GlobalSettingsTests : TestBase
{
    [TestMethod]
    public async Task TestAllowUserAdjustNicknameSetting()
    {
        // 1. Login as admin
        await LoginAsAdmin();

        // 2. Disable Allow_User_Adjust_Nickname
        using (var scope = Server.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            await settingsService.UpdateSettingAsync(SettingsMap.AllowUserAdjustNickname, "False");
        }

        // 3. Verify that the "Change your profile" link is NOT visible on Manage/Index
        var manageIndexResponse = await Http.GetAsync("/Manage/Index");
        var manageIndexHtml = await manageIndexResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Change your profile", manageIndexHtml);

        // 4. Verify that accessing /Manage/ChangeProfile directly returns BadRequest
        var changeProfileResponse = await Http.GetAsync("/Manage/ChangeProfile");
        Assert.AreEqual(HttpStatusCode.BadRequest, changeProfileResponse.StatusCode);

        // 5. Enable Allow_User_Adjust_Nickname
        using (var scope = Server.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            await settingsService.UpdateSettingAsync(SettingsMap.AllowUserAdjustNickname, "True");
        }

        // 6. Verify that the "Change your profile" link IS visible on Manage/Index
        manageIndexResponse = await Http.GetAsync("/Manage/Index");
        manageIndexHtml = await manageIndexResponse.Content.ReadAsStringAsync();
        Assert.Contains("Change your profile", manageIndexHtml);

        // 7. Verify that accessing /Manage/ChangeProfile directly returns OK
        changeProfileResponse = await Http.GetAsync("/Manage/ChangeProfile");
        Assert.AreEqual(HttpStatusCode.OK, changeProfileResponse.StatusCode);
    }

    [TestMethod]
    public async Task TestAdminManageSettings()
    {
        // 1. Login as admin
        await LoginAsAdmin();

        // 2. Access Global Settings Index
        var settingsResponse = await Http.GetAsync("/GlobalSettings/Index");
        settingsResponse.EnsureSuccessStatusCode();
        var settingsHtml = await settingsResponse.Content.ReadAsStringAsync();
        Assert.Contains("Global Settings", settingsHtml);
        Assert.Contains(SettingsMap.AllowUserAdjustNickname, settingsHtml);

        // 3. Change setting via UI
        var editResponse = await PostForm("/GlobalSettings/Edit", new Dictionary<string, string>
        {
            { "Key", SettingsMap.AllowUserAdjustNickname },
            { "Value", "False" }
        }, tokenUrl: "/GlobalSettings/Index");
        Assert.AreEqual(HttpStatusCode.Found, editResponse.StatusCode);

        // 4. Verify setting changed in DB
        using (var scope = Server.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            var value = await settingsService.GetBoolSettingAsync(SettingsMap.AllowUserAdjustNickname);
            Assert.IsFalse(value);
        }
    }

    [TestMethod]
    public async Task TestDatabaseFallbackAndCache()
    {
        using var scope = Server.Services.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        
        // Since it's not in Definitions, we can't update it via service easily without it being defined.
        // But we can test hitting the database for an existing key after clearing cache.
        
        await settingsService.UpdateSettingAsync(SettingsMap.Icp, "ICP123");
        Assert.AreEqual("ICP123", await settingsService.GetSettingValueAsync(SettingsMap.Icp));
        
        // Modify DB directly
        var setting = await dbContext.GlobalSettings.FirstAsync(s => s.Key == SettingsMap.Icp);
        setting.Value = "ICP456";
        await dbContext.SaveChangesAsync();
        
        // Should still be cached
        Assert.AreEqual("ICP123", await settingsService.GetSettingValueAsync(SettingsMap.Icp));
        
        // In a real scenario, cache expires or is cleared.
        // We can't easily clear the cache here without getting the IMemoryCache.
        var cache = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>();
        cache.Remove($"global-setting-{SettingsMap.Icp}");
        
        Assert.AreEqual("ICP456", await settingsService.GetSettingValueAsync(SettingsMap.Icp));
    }

    [TestMethod]
    public async Task TestGetIntSettingAsync()
    {
        using var scope = Server.Services.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        
        // Test with non-number value
        var value = await settingsService.GetIntSettingAsync(SettingsMap.ProjectName);
        Assert.AreEqual(0, value);
    }
    
    [TestMethod]
    public async Task TestGetBoolSettingAsyncInvalid()
    {
        using var scope = Server.Services.CreateScope();
        var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
        
        // Test with non-bool value
        var value = await settingsService.GetBoolSettingAsync(SettingsMap.ProjectName);
        Assert.IsFalse(value);
    }

    [TestMethod]
    public async Task TestUpdateSettingOverriddenByConfig()
    {
        // Start a new server with an overridden setting
        var port = Network.GetAvailablePort();
        Environment.SetEnvironmentVariable($"GlobalSettings__{SettingsMap.BrandName}", "OverriddenBrand");
        try
        {
            var server = await AppAsync<Startup>([], port: port);
            await server.UpdateDbAsync<TemplateDbContext>();
            await server.StartAsync();

            using var scope = server.Services.CreateScope();
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();

            // Verify it is overridden
            Assert.IsTrue(settingsService.IsOverriddenByConfig(SettingsMap.BrandName));
            Assert.AreEqual("OverriddenBrand", await settingsService.GetSettingValueAsync(SettingsMap.BrandName));

            // Try to update it
            try
            {
                await settingsService.UpdateSettingAsync(SettingsMap.BrandName, "NewValue");
                Assert.Fail("Should have thrown InvalidOperationException because it is overridden by config.");
            }
            catch (InvalidOperationException ex)
            {
                Assert.Contains("is overridden by configuration", ex.Message);
            }

            await server.StopAsync();
            await server.DisposeAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable($"GlobalSettings__{SettingsMap.BrandName}", null);
        }
    }
}
