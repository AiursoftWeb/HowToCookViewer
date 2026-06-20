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
public class LocalizeRecipesJobTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fake translation service: returns predictable translations.
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class FakeTranslationService : IRecipeTranslationService
    {
        public Task<string> TranslateAsync(string text, string targetLanguage)
        {
            return Task.FromResult($"[{targetLanguage}] {text}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SQLite in-memory context (InMemory provider doesn't support complex queries)
    // ─────────────────────────────────────────────────────────────────────────

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

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: build a configured LocalizeRecipesJob
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<LocalizeRecipesJob> CreateJobAsync(
        string languages = "en-US",
        string openAiInstance = "http://localhost:11434")
    {
        // Seed settings into a fresh context
        await using (var seedDb = new SqliteTestContext(_dbOptions))
        {
            seedDb.GlobalSettings.Add(new GlobalSetting
            {
                Key = SettingsMap.LocalizationLanguages,
                Value = languages
            });
            seedDb.GlobalSettings.Add(new GlobalSetting
            {
                Key = SettingsMap.OpenAiInstance,
                Value = openAiInstance
            });
            await seedDb.SaveChangesAsync();
        }

        // Create a new DB context for the job (owns its own context)
        var db = new SqliteTestContext(_dbOptions);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var settings = new GlobalSettingsService(db, config, null!, _cache);
        var translator = new FakeTranslationService();

        return new LocalizeRecipesJob(
            db,
            settings,
            translator,
            NullLogger<LocalizeRecipesJob>.Instance);
    }

    private static async Task SeedAsync(TemplateDbContext db, params object[] entities)
    {
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static Recipe CreateRecipe(int id, string name, DateTime fileLastModified,
        string description = "Test description",
        string ingredients = "Test ingredients",
        string calculation = "Test calculation",
        string steps = "Test steps",
        string notes = "Test notes")
    {
        return new Recipe
        {
            Id = id,
            Name = name,
            Category = "test",
            FilePath = $"dishes/test/{id}.md",
            Description = description,
            Ingredients = ingredients,
            Calculation = calculation,
            Steps = steps,
            Notes = notes,
            FileLastModified = fileLastModified
        };
    }

    private static LocalizedRecipe CreateLocalized(int id, int recipeId, string culture,
        DateTime lastLocalizedAt,
        string name = "Old translated name",
        string description = "Old translated description",
        string ingredients = "Old translated ingredients",
        string calculation = "Old translated calculation",
        string steps = "Old translated steps",
        string notes = "Old translated notes")
    {
        return new LocalizedRecipe
        {
            Id = id,
            RecipeId = recipeId,
            Culture = culture,
            LocalizedName = name,
            LocalizedDescription = description,
            LocalizedIngredients = ingredients,
            LocalizedCalculation = calculation,
            LocalizedSteps = steps,
            LocalizedNotes = notes,
            LastLocalizedAt = lastLocalizedAt
        };
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 1: Stale translation → cleared and re-translated
    // ═════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ExecuteAsync_ReLocalizesWhenSourceUpdated()
    {
        // Arrange: recipe was updated after the last localization
        var sourceUpdateTime = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var lastLocalizedTime = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc); // 1 day older

        var recipe = CreateRecipe(1, "西红柿炒鸡蛋", sourceUpdateTime);
        var oldLocalized = CreateLocalized(1, 1, "en-US", lastLocalizedTime,
            name: "Old Name",
            description: "Old Desc",
            ingredients: "Old Ingredients",
            calculation: "Old Calc",
            steps: "Old Steps",
            notes: "Old Notes");

        var job = await CreateJobAsync(languages: "en-US");
        await using var seedDb = new SqliteTestContext(_dbOptions);
        await SeedAsync(seedDb, recipe, oldLocalized);

        // Act
        await job.ExecuteAsync();

        // Assert: the localized content must be from the fake translator,
        // NOT the old content.
        await using var assertDb = new SqliteTestContext(_dbOptions);
        var localized = await assertDb.LocalizedRecipes
            .FirstAsync(lr => lr.RecipeId == 1 && lr.Culture == "en-US");

        Assert.AreEqual("[en-US] 西红柿炒鸡蛋", localized.LocalizedName,
            "Name must be re-translated, not the old value.");
        Assert.AreEqual("[en-US] Test description", localized.LocalizedDescription,
            "Description must be re-translated.");
        Assert.AreEqual("[en-US] Test ingredients", localized.LocalizedIngredients,
            "Ingredients must be re-translated.");
        Assert.AreEqual("[en-US] Test calculation", localized.LocalizedCalculation,
            "Calculation must be re-translated.");
        Assert.AreEqual("[en-US] Test steps", localized.LocalizedSteps,
            "Steps must be re-translated.");
        Assert.AreEqual("[en-US] Test notes", localized.LocalizedNotes,
            "Notes must be re-translated.");
        Assert.AreNotEqual(DateTime.MinValue, localized.LastLocalizedAt,
            "LastLocalizedAt must be updated to a current timestamp.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 2: Current translation → skipped entirely
    // ═════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ExecuteAsync_SkipsRecipeWhenTranslationIsCurrent()
    {
        // Arrange: translation was done AFTER the source was last modified
        var sourceUpdateTime = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
        var lastLocalizedTime = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc); // newer than source

        var recipe = CreateRecipe(1, "西红柿炒鸡蛋", sourceUpdateTime);
        var currentLocalized = CreateLocalized(1, 1, "en-US", lastLocalizedTime,
            name: "Current Name",
            description: "Current Desc",
            ingredients: "Current Ingredients",
            calculation: "Current Calc",
            steps: "Current Steps",
            notes: "Current Notes");

        var job = await CreateJobAsync(languages: "en-US");
        await using var seedDb = new SqliteTestContext(_dbOptions);
        await SeedAsync(seedDb, recipe, currentLocalized);

        // Act
        await job.ExecuteAsync();

        // Assert: the localized content must be UNCHANGED
        await using var assertDb = new SqliteTestContext(_dbOptions);
        var localized = await assertDb.LocalizedRecipes
            .FirstAsync(lr => lr.RecipeId == 1 && lr.Culture == "en-US");

        Assert.AreEqual("Current Name", localized.LocalizedName,
            "Name must remain unchanged when translation is already current.");
        Assert.AreEqual("Current Desc", localized.LocalizedDescription);
        Assert.AreEqual("Current Ingredients", localized.LocalizedIngredients);
        Assert.AreEqual("Current Calc", localized.LocalizedCalculation);
        Assert.AreEqual("Current Steps", localized.LocalizedSteps);
        Assert.AreEqual("Current Notes", localized.LocalizedNotes);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 3: New recipe with no existing localization → fresh translation
    // ═════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ExecuteAsync_LocalizesNewRecipeWithNoExistingTranslation()
    {
        // Arrange: recipe exists but has NO LocalizedRecipe row
        var sourceUpdateTime = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var recipe = CreateRecipe(1, "新菜谱", sourceUpdateTime);

        var job = await CreateJobAsync(languages: "en-US");
        await using var seedDb = new SqliteTestContext(_dbOptions);
        await SeedAsync(seedDb, recipe);

        // Act
        await job.ExecuteAsync();

        // Assert: a new LocalizedRecipe was created with fresh translations
        await using var assertDb = new SqliteTestContext(_dbOptions);
        var localized = await assertDb.LocalizedRecipes
            .FirstOrDefaultAsync(lr => lr.RecipeId == 1 && lr.Culture == "en-US");

        Assert.IsNotNull(localized, "A new LocalizedRecipe row must be created.");
        Assert.AreEqual("[en-US] 新菜谱", localized.LocalizedName);
        Assert.AreNotEqual(DateTime.MinValue, localized.LastLocalizedAt,
            "LastLocalizedAt must be set after successful translation.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 4: Multiple cultures — each gets its own localization
    // ═════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ExecuteAsync_LocalizesMultipleCultures()
    {
        var sourceUpdateTime = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var recipe = CreateRecipe(1, "西红柿炒鸡蛋", sourceUpdateTime);

        var job = await CreateJobAsync(languages: "en-US,ja-JP");
        await using var seedDb = new SqliteTestContext(_dbOptions);
        await SeedAsync(seedDb, recipe);

        // Act
        await job.ExecuteAsync();

        // Assert: both cultures get translations
        await using var assertDb = new SqliteTestContext(_dbOptions);
        var enRow = await assertDb.LocalizedRecipes
            .FirstOrDefaultAsync(lr => lr.RecipeId == 1 && lr.Culture == "en-US");
        var jaRow = await assertDb.LocalizedRecipes
            .FirstOrDefaultAsync(lr => lr.RecipeId == 1 && lr.Culture == "ja-JP");

        Assert.IsNotNull(enRow, "en-US translation must exist.");
        Assert.IsNotNull(jaRow, "ja-JP translation must exist.");
        Assert.AreEqual("[en-US] 西红柿炒鸡蛋", enRow.LocalizedName);
        Assert.AreEqual("[ja-JP] 西红柿炒鸡蛋", jaRow.LocalizedName);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 5: AI disabled → entire job is skipped
    // ═════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ExecuteAsync_SkipsWhenAiDisabled()
    {
        var sourceUpdateTime = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var recipe = CreateRecipe(1, "西红柿炒鸡蛋", sourceUpdateTime);

        // OpenAiInstance is empty → AI localization is disabled
        var job = await CreateJobAsync(languages: "en-US", openAiInstance: "");
        await using var seedDb = new SqliteTestContext(_dbOptions);
        await SeedAsync(seedDb, recipe);

        // Act
        await job.ExecuteAsync();

        // Assert: no localization was created
        await using var assertDb = new SqliteTestContext(_dbOptions);
        var anyLocalized = await assertDb.LocalizedRecipes.AnyAsync();
        Assert.IsFalse(anyLocalized, "No localization should be created when AI is disabled.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 6: Source updated → only changed recipe fields are re-translated
    //         (unchanged fields keep their old translations... wait, no!
    //          With our fix, ALL fields are cleared and re-translated when
    //          FileLastModified > LastLocalizedAt — verifying that here.)
    // ═════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ExecuteAsync_ClearsAndReTranslatesAllFieldsWhenSourceUpdated()
    {
        // Arrange: source was updated, but the old localization still exists.
        // Only SOME fields changed in the source (e.g. Name changed, Steps changed).
        // But ALL localized fields must be refreshed regardless.
        var sourceUpdateTime = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var lastLocalizedTime = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);

        // Source has different content than what was previously translated
        var recipe = CreateRecipe(1, "Updated Name", sourceUpdateTime,
            description: "Updated Desc",
            ingredients: "Updated Ingredients",
            calculation: "Updated Calc",
            steps: "Updated Steps",
            notes: "Updated Notes");

        // Old localization had different (stale) content
        var oldLocalized = CreateLocalized(1, 1, "en-US", lastLocalizedTime,
            name: "Stale Name",
            description: "Stale Desc",
            ingredients: "Stale Ingredients",
            calculation: "Stale Calc",
            steps: "Stale Steps",
            notes: "Stale Notes");

        var job = await CreateJobAsync(languages: "en-US");
        await using var seedDb = new SqliteTestContext(_dbOptions);
        await SeedAsync(seedDb, recipe, oldLocalized);

        // Act
        await job.ExecuteAsync();

        // Assert: EVERY field reflects the fake translator output
        // (which means they were cleared and re-translated, not left as stale content)
        await using var assertDb = new SqliteTestContext(_dbOptions);
        var localized = await assertDb.LocalizedRecipes
            .FirstAsync(lr => lr.RecipeId == 1 && lr.Culture == "en-US");

        Assert.AreEqual("[en-US] Updated Name", localized.LocalizedName);
        Assert.AreEqual("[en-US] Updated Desc", localized.LocalizedDescription);
        Assert.AreEqual("[en-US] Updated Ingredients", localized.LocalizedIngredients);
        Assert.AreEqual("[en-US] Updated Calc", localized.LocalizedCalculation);
        Assert.AreEqual("[en-US] Updated Steps", localized.LocalizedSteps);
        Assert.AreEqual("[en-US] Updated Notes", localized.LocalizedNotes);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 7: No languages configured → job is skipped
    // ═════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ExecuteAsync_SkipsWhenNoLanguagesConfigured()
    {
        var sourceUpdateTime = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var recipe = CreateRecipe(1, "西红柿炒鸡蛋", sourceUpdateTime);

        var job = await CreateJobAsync(languages: "");
        await using var seedDb = new SqliteTestContext(_dbOptions);
        await SeedAsync(seedDb, recipe);

        // Act
        await job.ExecuteAsync();

        // Assert: nothing was localized
        await using var assertDb = new SqliteTestContext(_dbOptions);
        var anyLocalized = await assertDb.LocalizedRecipes.AnyAsync();
        Assert.IsFalse(anyLocalized, "No localization when languages are empty.");
    }
}
