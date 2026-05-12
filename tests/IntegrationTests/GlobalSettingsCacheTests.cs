using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Services;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class GlobalSettingsCacheTests : TestBase
{
    [TestMethod]
    public async Task TestGlobalSettingsCaching()
    {
        var key = SettingsMap.AllowUserAdjustNickname;
        
        // 1. Initial value (should be seeded)
        string initialValue;
        using (var scope = Server.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            initialValue = await settingsService.GetSettingValueAsync(key);
        }

        // 2. Modify database directly (bypass service/cache)
        using (var scope = Server.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var dbSetting = await dbContext.GlobalSettings.FirstAsync(s => s.Key == key);
            dbSetting.Value = initialValue == "True" ? "False" : "True";
            await dbContext.SaveChangesAsync();
        }

        // 3. Get value again from service - should be cached (initialValue)
        using (var scope = Server.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            var cachedValue = await settingsService.GetSettingValueAsync(key);
            Assert.AreEqual(initialValue, cachedValue, "Value should have been served from cache, not database.");
        }

        // 4. Update via service - should clear cache
        var newValue = initialValue == "True" ? "False" : "True";
        using (var scope = Server.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            await settingsService.UpdateSettingAsync(key, newValue);
        }

        // 5. Get value again - should be new value
        using (var scope = Server.Services.CreateScope())
        {
            var settingsService = scope.ServiceProvider.GetRequiredService<GlobalSettingsService>();
            var updatedValue = await settingsService.GetSettingValueAsync(key);
            Assert.AreEqual(newValue, updatedValue, "Value should be updated after clearing cache.");
        }
    }
}
