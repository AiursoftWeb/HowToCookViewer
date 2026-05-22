using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Services.BackgroundJobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class CleanupLocalizedRecipesJobTests
{
    // Concrete context for SQLite in-memory tests — enables ExecuteDeleteAsync
    // which the InMemory provider does not support.
    private sealed class SqliteTestContext(DbContextOptions<SqliteTestContext> options)
        : TemplateDbContext(options)
    {
    }

    private SqliteConnection _connection = null!;
    private DbContextOptions<SqliteTestContext> _dbOptions = null!;
    private IMemoryCache _cache = null!;

    [TestInitialize]
    public void Initialize()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbOptions = new DbContextOptionsBuilder<SqliteTestContext>()
            .UseSqlite(_connection)
            .Options;

        _cache = new MemoryCache(new MemoryCacheOptions());

        using var db = new SqliteTestContext(_dbOptions);
        db.Database.EnsureCreated();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _connection.Close();
        _connection.Dispose();
    }

    private async Task<CleanupLocalizedRecipesJob> CreateJobAsync(string languages = "en,ja")
    {
        // Seed the language setting into a fresh context
        await using (var seedDb = new SqliteTestContext(_dbOptions))
        {
            seedDb.GlobalSettings.Add(new GlobalSetting
            {
                Key = SettingsMap.LocalizationLanguages,
                Value = languages
            });
            await seedDb.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // The job owns this context (disposed when the test cleans up)
        var db = new SqliteTestContext(_dbOptions);
        var settings = new GlobalSettingsService(db, config, null!, _cache);

        return new CleanupLocalizedRecipesJob(
            db,
            settings,
            NullLogger<CleanupLocalizedRecipesJob>.Instance);
    }

    private static async Task SeedAsync(TemplateDbContext db, params object[] entities)
    {
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static Recipe CreateRecipe(int id, string name = "Test Recipe", bool deleted = false)
    {
        return new Recipe
        {
            Id = id,
            Name = name,
            Category = "test",
            FilePath = $"dishes/test/{id}.md",
            FileLastModified = DateTime.UtcNow,
            IsDeleted = deleted
        };
    }

    private static LocalizedRecipe CreateLocalized(int id, int recipeId, string culture, DateTime? lastLocalized = null)
    {
        return new LocalizedRecipe
        {
            Id = id,
            RecipeId = recipeId,
            Culture = culture,
            LocalizedName = $"Recipe {recipeId} in {culture}",
            LastLocalizedAt = lastLocalized ?? DateTime.UtcNow
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // 1. Deletes orphaned rows (parent Recipe soft-deleted)
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_DeletesOrphanedLocalizedRecipes()
    {
        var job = await CreateJobAsync();
        await using var db = new SqliteTestContext(_dbOptions);

        var deletedRecipe = CreateRecipe(1, "Deleted Recipe", deleted: true);
        var activeRecipe = CreateRecipe(2, "Active Recipe", deleted: false);
        var orphaned = CreateLocalized(1, 1, "en", DateTime.UtcNow.AddHours(-1));
        var validRow = CreateLocalized(2, 2, "en", DateTime.UtcNow.AddHours(-1));

        await SeedAsync(db, deletedRecipe, activeRecipe, orphaned, validRow);

        // Act
        await job.ExecuteAsync();

        // Assert
        var remaining = await db.LocalizedRecipes.IgnoreQueryFilters().ToListAsync();
        Assert.AreEqual(1, remaining.Count, "Only the valid row should remain.");
        Assert.AreEqual(2, remaining[0].RecipeId, "Active recipe's localized row should survive.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 2. Deletes rows for cultures no longer configured
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_DeletesStaleCultureRows()
    {
        var job = await CreateJobAsync("en"); // only "en" configured
        await using var db = new SqliteTestContext(_dbOptions);

        var recipe = CreateRecipe(1, "Active Recipe");
        var enRow = CreateLocalized(1, 1, "en", DateTime.UtcNow.AddHours(-1));
        var jaRow = CreateLocalized(2, 1, "ja", DateTime.UtcNow.AddHours(-1));

        await SeedAsync(db, recipe, enRow, jaRow);

        // Act
        await job.ExecuteAsync();

        // Assert
        var remaining = await db.LocalizedRecipes.IgnoreQueryFilters().ToListAsync();
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual("en", remaining[0].Culture, "Only the configured culture row should survive.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 3. Does NOT delete rows for active cultures
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_KeepsActiveCultureRows()
    {
        var job = await CreateJobAsync("en,ja,zh");
        await using var db = new SqliteTestContext(_dbOptions);

        var recipe = CreateRecipe(1, "Active Recipe");
        var enRow = CreateLocalized(1, 1, "en", DateTime.UtcNow.AddHours(-2));
        var jaRow = CreateLocalized(2, 1, "ja", DateTime.UtcNow.AddHours(-2));

        await SeedAsync(db, recipe, enRow, jaRow);

        // Act
        await job.ExecuteAsync();

        // Assert
        var remaining = await db.LocalizedRecipes.IgnoreQueryFilters().ToListAsync();
        Assert.AreEqual(2, remaining.Count, "All active culture rows should remain.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 4. Staleness guard: fresh orphaned rows survive
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_KeepsFreshOrphanedRows()
    {
        var job = await CreateJobAsync("en");
        await using var db = new SqliteTestContext(_dbOptions);

        var deletedRecipe = CreateRecipe(1, "Deleted", deleted: true);
        var freshOrphan = CreateLocalized(1, 1, "en", DateTime.UtcNow);

        await SeedAsync(db, deletedRecipe, freshOrphan);

        // Act
        await job.ExecuteAsync();

        // Assert — fresh row survives the staleness guard
        var remaining = await db.LocalizedRecipes.IgnoreQueryFilters().ToListAsync();
        Assert.AreEqual(1, remaining.Count,
            "Freshly-created orphan row must survive the staleness guard " +
            "so a concurrently-running LocalizeRecipesJob is not undone.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 5. Staleness guard: fresh stale-culture rows survive
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_KeepsFreshStaleCultureRows()
    {
        var job = await CreateJobAsync("en"); // "ja" is not configured
        await using var db = new SqliteTestContext(_dbOptions);

        var recipe1 = CreateRecipe(1, "Active Recipe 1");
        var recipe2 = CreateRecipe(2, "Active Recipe 2");
        var freshJaRow = CreateLocalized(1, 1, "ja", DateTime.UtcNow);
        var oldJaRow = CreateLocalized(2, 2, "ja", DateTime.UtcNow.AddHours(-2));

        await SeedAsync(db, recipe1, recipe2, freshJaRow, oldJaRow);

        // Act
        await job.ExecuteAsync();

        // Assert
        var remaining = await db.LocalizedRecipes.IgnoreQueryFilters().ToListAsync();
        Assert.AreEqual(1, remaining.Count,
            "Only the fresh stale-culture row should survive the staleness guard.");
        Assert.AreEqual(freshJaRow.Id, remaining[0].Id);
    }

    // ─────────────────────────────────────────────────────────────────────
    // 6. Empty languages configuration — culture cleanup skipped
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_EmptyLanguagesSetting_DoesNotDeleteByCulture()
    {
        var job = await CreateJobAsync("");
        await using var db = new SqliteTestContext(_dbOptions);

        var recipe = CreateRecipe(1, "Active Recipe");
        var enRow = CreateLocalized(1, 1, "en", DateTime.UtcNow.AddHours(-2));

        await SeedAsync(db, recipe, enRow);

        // Act
        await job.ExecuteAsync();

        // Assert — culture cleanup is skipped when configuredCultures is empty,
        // and this row is not orphaned, so it must survive.
        var remaining = await db.LocalizedRecipes.IgnoreQueryFilters().ToListAsync();
        Assert.AreEqual(1, remaining.Count,
            "Rows for non-orphaned recipes must survive when no languages are configured.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // 7. Both deletions happen in one run
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExecuteAsync_DeletesBothOrphanedAndStaleCultureInOneRun()
    {
        var job = await CreateJobAsync("en"); // only "en" configured
        await using var db = new SqliteTestContext(_dbOptions);

        var deletedRecipe = CreateRecipe(1, "Deleted", deleted: true);
        var activeRecipe = CreateRecipe(2, "Active Recipe");
        var orphanedRow = CreateLocalized(1, 1, "en", DateTime.UtcNow.AddHours(-2));
        var staleJaRow = CreateLocalized(2, 2, "ja", DateTime.UtcNow.AddHours(-2));
        var validRow = CreateLocalized(3, 2, "en", DateTime.UtcNow.AddHours(-2));

        await SeedAsync(db, deletedRecipe, activeRecipe, orphanedRow, staleJaRow, validRow);

        // Act
        await job.ExecuteAsync();

        // Assert
        var remaining = await db.LocalizedRecipes.IgnoreQueryFilters().ToListAsync();
        Assert.AreEqual(1, remaining.Count, "Only the valid row should survive.");
        Assert.AreEqual(validRow.Id, remaining[0].Id);
    }
}
