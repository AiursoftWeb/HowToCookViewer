using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.MySql;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Services.BackgroundJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.HowToCookViewer.Tests;

/// <summary>
/// MySQL integration test for CleanupLocalizedRecipesJob.
/// You need a local MySQL: docker run -d --name htc-mysql-test -e MYSQL_ROOT_PASSWORD=test123 -e MYSQL_DATABASE=HowToCookViewer -p 3307:3306 hub.aiursoft.com/mysql:9.7.0
/// </summary>
[TestClass]
public class CleanupLocalizedRecipesJobMySqlTests
{
    private const string ConnectionString = "Server=localhost;Port=3307;Database=HowToCookViewer;Uid=root;Pwd=test123;";

    private DbContextOptions<MySqlContext> CreateOptions() =>
        new DbContextOptionsBuilder<MySqlContext>()
            .UseMySql(ConnectionString, new MySqlServerVersion(new Version(9, 7, 0)))
            .Options;

    [TestInitialize]
    public async Task Initialize()
    {
        await using var db = new MySqlContext(CreateOptions());
        try
        {
            await db.Database.EnsureCreatedAsync();
        }
        catch (Exception ex) when (ex is MySqlConnector.MySqlException or InvalidOperationException or System.Net.Sockets.SocketException)
        {
            Assert.Inconclusive($"MySQL is not available on localhost:3307. Skipping integration test. ({ex.GetType().Name})");
        }
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await using var db = new MySqlContext(CreateOptions());
        try
        {
            await db.Database.EnsureDeletedAsync();
        }
        catch
        {
            // MySQL not available — nothing to clean up.
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_MySql_DeletesOrphanedAndStaleRows_WithoutError1093()
    {
        // Seed
        await using (var db = new MySqlContext(CreateOptions()))
        {
            db.GlobalSettings.Add(new GlobalSetting { Key = SettingsMap.LocalizationLanguages, Value = "en" });
            db.Recipes.Add(new Recipe { Id = 1, Name = "Deleted", Category = "test", FilePath = "d/1.md", FileLastModified = DateTime.UtcNow, IsDeleted = true });
            db.Recipes.Add(new Recipe { Id = 2, Name = "Active", Category = "test", FilePath = "d/2.md", FileLastModified = DateTime.UtcNow });
            db.LocalizedRecipes.Add(new LocalizedRecipe { Id = 1, RecipeId = 1, Culture = "en", LocalizedName = "Orphaned", LastLocalizedAt = DateTime.UtcNow.AddHours(-2) });
            db.LocalizedRecipes.Add(new LocalizedRecipe { Id = 2, RecipeId = 2, Culture = "ja", LocalizedName = "StaleJA", LastLocalizedAt = DateTime.UtcNow.AddHours(-2) });
            db.LocalizedRecipes.Add(new LocalizedRecipe { Id = 3, RecipeId = 2, Culture = "en", LocalizedName = "Valid", LastLocalizedAt = DateTime.UtcNow.AddHours(-2) });
            await db.SaveChangesAsync();
        }

        // Act
        await using (var db = new MySqlContext(CreateOptions()))
        {
            var cache = new MemoryCache(new MemoryCacheOptions());
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
            var gss = new GlobalSettingsService(db, config, null!, cache);
            var job = new CleanupLocalizedRecipesJob(db, gss, NullLogger<CleanupLocalizedRecipesJob>.Instance);

            await job.ExecuteAsync();
        }

        // Assert
        await using (var db = new MySqlContext(CreateOptions()))
        {
            var remaining = await db.LocalizedRecipes.IgnoreQueryFilters().ToListAsync();
            Assert.AreEqual(1, remaining.Count, $"Expected 1 row, got {remaining.Count}: {string.Join(", ", remaining.Select(r => $"#{r.Id}"))}");
            Assert.AreEqual(3, remaining[0].Id, "Only the valid row (#3) should survive.");
            Assert.AreEqual(2, remaining[0].RecipeId);
            Assert.AreEqual("en", remaining[0].Culture);
        }
    }
}
