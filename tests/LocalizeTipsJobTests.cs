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
public class LocalizeTipsJobTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fake translation service
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class FakeTranslationService : IRecipeTranslationService
    {
        public Task<string> TranslateAsync(string text, string targetLanguage)
        {
            return Task.FromResult($"[{targetLanguage}] {text}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SQLite in-memory context
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

    private async Task<LocalizeTipsJob> CreateJobAsync(
        string languages = "en-US",
        string openAiInstance = "http://localhost:11434")
    {
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

        var db = new SqliteTestContext(_dbOptions);
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var settings = new GlobalSettingsService(db, config, null!, _cache);
        var translator = new FakeTranslationService();

        return new LocalizeTipsJob(
            db,
            settings,
            translator,
            NullLogger<LocalizeTipsJob>.Instance);
    }

    private static async Task SeedAsync(TemplateDbContext db, params object[] entities)
    {
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private static Tip CreateTip(int id, string title, DateTime fileLastModified,
        string content = "Tip content text")
    {
        return new Tip
        {
            Id = id,
            Title = title,
            Category = "advanced",
            FilePath = $"tips/advanced/{id}.md",
            Content = content,
            FileLastModified = fileLastModified
        };
    }

    private static LocalizedTip CreateLocalizedTip(int id, int tipId, string culture,
        DateTime lastLocalizedAt,
        string title = "Old translated title",
        string content = "Old translated content")
    {
        return new LocalizedTip
        {
            Id = id,
            TipId = tipId,
            Culture = culture,
            LocalizedTitle = title,
            LocalizedContent = content,
            LastLocalizedAt = lastLocalizedAt
        };
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 1: Stale tip translation → cleared and re-translated
    // ═════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ExecuteAsync_ReLocalizesTipWhenSourceUpdated()
    {
        var sourceUpdateTime = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var lastLocalizedTime = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);

        var tip = CreateTip(1, "油温判断技巧", sourceUpdateTime);
        var oldLocalized = CreateLocalizedTip(1, 1, "en-US", lastLocalizedTime,
            title: "Old Title",
            content: "Old Content");

        var job = await CreateJobAsync(languages: "en-US");
        await using var seedDb = new SqliteTestContext(_dbOptions);
        await SeedAsync(seedDb, tip, oldLocalized);

        // Act
        await job.ExecuteAsync();

        // Assert: re-translated content must be from the fake translator
        await using var assertDb = new SqliteTestContext(_dbOptions);
        var localized = await assertDb.LocalizedTips
            .FirstAsync(lt => lt.TipId == 1 && lt.Culture == "en-US");

        Assert.AreEqual("[en-US] 油温判断技巧", localized.LocalizedTitle,
            "Title must be re-translated.");
        Assert.AreEqual("[en-US] Tip content text", localized.LocalizedContent,
            "Content must be re-translated.");
        Assert.AreNotEqual(DateTime.MinValue, localized.LastLocalizedAt,
            "LastLocalizedAt must be updated.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 2: Current tip translation → skipped
    // ═════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ExecuteAsync_SkipsTipWhenTranslationIsCurrent()
    {
        var sourceUpdateTime = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
        var lastLocalizedTime = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);

        var tip = CreateTip(1, "油温判断技巧", sourceUpdateTime);
        var currentLocalized = CreateLocalizedTip(1, 1, "en-US", lastLocalizedTime,
            title: "Current Title",
            content: "Current Content");

        var job = await CreateJobAsync(languages: "en-US");
        await using var seedDb = new SqliteTestContext(_dbOptions);
        await SeedAsync(seedDb, tip, currentLocalized);

        // Act
        await job.ExecuteAsync();

        // Assert: content must be unchanged
        await using var assertDb = new SqliteTestContext(_dbOptions);
        var localized = await assertDb.LocalizedTips
            .FirstAsync(lt => lt.TipId == 1 && lt.Culture == "en-US");

        Assert.AreEqual("Current Title", localized.LocalizedTitle,
            "Title must remain unchanged when translation is current.");
        Assert.AreEqual("Current Content", localized.LocalizedContent,
            "Content must remain unchanged when translation is current.");
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 3: New tip → fresh translation
    // ═════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ExecuteAsync_LocalizesNewTipWithNoExistingTranslation()
    {
        var sourceUpdateTime = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var tip = CreateTip(1, "新技巧", sourceUpdateTime);

        var job = await CreateJobAsync(languages: "en-US");
        await using var seedDb = new SqliteTestContext(_dbOptions);
        await SeedAsync(seedDb, tip);

        // Act
        await job.ExecuteAsync();

        // Assert
        await using var assertDb = new SqliteTestContext(_dbOptions);
        var localized = await assertDb.LocalizedTips
            .FirstOrDefaultAsync(lt => lt.TipId == 1 && lt.Culture == "en-US");

        Assert.IsNotNull(localized, "A new LocalizedTip row must be created.");
        Assert.AreEqual("[en-US] 新技巧", localized.LocalizedTitle);
        Assert.AreNotEqual(DateTime.MinValue, localized.LastLocalizedAt);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Test 4: Source updated → both title and content cleared and re-translated
    // ═════════════════════════════════════════════════════════════════════════

    [TestMethod]
    public async Task ExecuteAsync_ClearsAndReTranslatesBothFieldsWhenSourceUpdated()
    {
        var sourceUpdateTime = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
        var lastLocalizedTime = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);

        var tip = CreateTip(1, "Updated Title", sourceUpdateTime, content: "Updated Content");
        var oldLocalized = CreateLocalizedTip(1, 1, "en-US", lastLocalizedTime,
            title: "Stale Title",
            content: "Stale Content");

        var job = await CreateJobAsync(languages: "en-US");
        await using var seedDb = new SqliteTestContext(_dbOptions);
        await SeedAsync(seedDb, tip, oldLocalized);

        // Act
        await job.ExecuteAsync();

        // Assert: both fields reflect the fake translator output
        await using var assertDb = new SqliteTestContext(_dbOptions);
        var localized = await assertDb.LocalizedTips
            .FirstAsync(lt => lt.TipId == 1 && lt.Culture == "en-US");

        Assert.AreEqual("[en-US] Updated Title", localized.LocalizedTitle);
        Assert.AreEqual("[en-US] Updated Content", localized.LocalizedContent);
    }
}
