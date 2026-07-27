using System.Net;
using System.Security.Cryptography;
using System.Text;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.InMemory;
using Aiursoft.HowToCookViewer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class RecipeVectorSearchTests
{
    private const int VectorDimension = 1024;

    private InMemoryContext _db = null!;
    private RecipeEmbeddingCache _cache = null!;
    private IConfiguration _config = null!;
    private IMemoryCache _memoryCache = null!;

    // ─────────────────────────────────────────────────────────────────────────
    // Test setup / helpers
    // ─────────────────────────────────────────────────────────────────────────

    [TestInitialize]
    public void Initialize()
    {
        var dbName = "VectorSearchTest_" + Guid.NewGuid();
        var dbOptions = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        _db = new InMemoryContext(dbOptions);
        _cache = new RecipeEmbeddingCache(new LoggerFactory().CreateLogger<RecipeEmbeddingCache>());
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
    }

    private async Task SeedGlobalSettingsAsync(
        bool useAiSearch = false,
        string ollamaInstance = "",
        string embeddingModel = "",
        int cacheLimit = 2000)
    {
        var settings = new[]
        {
            new GlobalSetting { Key = SettingsMap.EnableEmbeddingBasedSearch, Value = useAiSearch ? "True" : "False" },
            new GlobalSetting { Key = SettingsMap.OpenAiInstance, Value = ollamaInstance },
            new GlobalSetting { Key = SettingsMap.OpenAiLocalizationModel, Value = "" },
            new GlobalSetting { Key = SettingsMap.OpenAiApiToken, Value = "" },
            new GlobalSetting { Key = SettingsMap.EmbeddingModel, Value = embeddingModel },
            new GlobalSetting { Key = SettingsMap.EmbeddingQueryCacheLimit, Value = cacheLimit.ToString() }
        };

        foreach (var setting in settings)
        {
            var existing = await _db.GlobalSettings.FirstOrDefaultAsync(s => s.Key == setting.Key);
            if (existing != null)
            {
                existing.Value = setting.Value;
            }
            else
            {
                _db.GlobalSettings.Add(setting);
            }
        }

        await _db.SaveChangesAsync();
    }

    private GlobalSettingsService CreateSettingsService()
    {
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        return new GlobalSettingsService(_db, _config, null!, _memoryCache);
    }

    /// <summary>
    /// Seeds recipes with known embeddings so the cache has data for vector search.
    /// Recipe "Noodle Soup" gets a vector close to the query vector.
    /// Recipe "Bread" gets a vector far from the query vector.
    /// </summary>
    private async Task SeedRecipesWithEmbeddingsAsync()
    {
        // Vector close to noodle query.
        var noodleSoupVector = EncodeVector(v => { v[0] = 0.9f; v[1] = 0.9f; });
        // Vector far from noodle query (orthogonal-ish).
        var breadVector = EncodeVector(v => { v[2] = 1.0f; v[3] = 1.0f; });

        _db.Recipes.Add(new Recipe
        {
            Name = "牛肉面",
            Category = "noodle_dish",
            FilePath = "dishes/noodle_dish/beef_noodle.md",
            Description = "一碗香喷喷的牛肉面",
            FileLastModified = DateTime.UtcNow,
            Embedding = noodleSoupVector,
            LastEmbeddedAt = DateTime.UtcNow
        });

        _db.Recipes.Add(new Recipe
        {
            Name = "面包",
            Category = "pastry",
            FilePath = "dishes/pastry/bread.md",
            Description = "自制手工面包",
            FileLastModified = DateTime.UtcNow,
            Embedding = breadVector,
            LastEmbeddedAt = DateTime.UtcNow
        });

        _db.Recipes.Add(new Recipe
        {
            Name = "未向量化的菜",
            Category = "vegetable_dish",
            FilePath = "dishes/vegetable/no_embedding.md",
            Description = "这道菜还没有生成向量",
            FileLastModified = DateTime.UtcNow,
            Embedding = null,
            LastEmbeddedAt = DateTime.MinValue
        });

        await _db.SaveChangesAsync();
        await _cache.LoadAsync(_db);
    }

    private static byte[] EncodeVector(Action<float[]> initialize)
    {
        var vector = new float[VectorDimension];
        initialize(vector);
        Normalize(vector);
        var bytes = new byte[VectorDimension * 4];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static void Normalize(float[] vector)
    {
        var sumSq = 0f;
        for (var i = 0; i < vector.Length; i++)
            sumSq += vector[i] * vector[i];
        var norm = MathF.Sqrt(sumSq);
        if (norm > 0)
        {
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;
        }
    }

    private static string HashKey(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var sb = new StringBuilder(40);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2"));
            if (sb.Length >= 40) break;
        }
        return sb.ToString();
    }

    private RecipeVectorSearchService CreateSearchService(HttpMessageHandler? handler = null)
    {
        var settings = CreateSettingsService();
        var httpClientFactory = handler != null
            ? new TestHttpClientFactory(handler)
            : (IHttpClientFactory)new TestHttpClientFactory(new FakeOllamaEmbedHandler());
        return new RecipeVectorSearchService(_db, _cache, settings, httpClientFactory, new LoggerFactory().CreateLogger<RecipeVectorSearchService>());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tests: three conditions for AI vector search
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Condition1_UseAiSearchDisabled_ReturnsUsedAiFalse()
    {
        await SeedGlobalSettingsAsync(useAiSearch: false, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");
        await SeedRecipesWithEmbeddingsAsync();

        var service = CreateSearchService();
        var (usedAi, results, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "牛肉面", 1, 10);

        Assert.IsFalse(usedAi, "Should NOT use AI search when UseAiSearch is false.");
        Assert.AreEqual(0, results.Count, "Results should be empty when AI search is skipped.");
    }

    [TestMethod]
    public async Task Condition2_OllamaInstanceNotConfigured_ReturnsUsedAiFalse()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "", embeddingModel: "bge-m3");
        await SeedRecipesWithEmbeddingsAsync();

        var service = CreateSearchService();
        var (usedAi, results, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "牛肉面", 1, 10);

        Assert.IsFalse(usedAi, "Should NOT use AI search when OllamaInstance is empty.");
        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task Condition3_EmbeddingModelNotConfigured_ReturnsUsedAiFalse()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "");
        await SeedRecipesWithEmbeddingsAsync();

        var service = CreateSearchService();
        var (usedAi, results, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "牛肉面", 1, 10);

        Assert.IsFalse(usedAi, "Should NOT use AI search when EmbeddingModel is empty.");
        Assert.AreEqual(0, results.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tests: successful vector search
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task AllConditionsMet_VectorSearchSucceeds()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");
        await SeedRecipesWithEmbeddingsAsync();

        // Fake Ollama that returns a vector close to "noodle" recipes.
        var fakeHandler = new FakeOllamaEmbedHandler(v =>
        {
            v[0] = 1.0f;
            v[1] = 1.0f;
        });

        var service = CreateSearchService(fakeHandler);
        var (usedAi, results, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "面", 1, 10);

        Assert.IsTrue(usedAi, "Should use AI search when all conditions are met.");
        Assert.IsTrue(results.Count > 0, "Should return at least one result.");
        // "牛肉面" (noodle soup) should rank higher than "面包" (bread) for query "面" (noodles).
        if (results.Count >= 2)
        {
            Assert.AreEqual("牛肉面", results[0].Name,
                "Semantic search should rank noodle soup above bread for query '面' (noodles).");
        }
    }

    [TestMethod]
    public async Task OllamaTimeout_FallsBackToUsedAiFalse()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");
        await SeedRecipesWithEmbeddingsAsync();

        // Handler that delays 15 seconds (exceeds 10s embed timeout).
        var slowHandler = new SlowOllamaHandler(TimeSpan.FromSeconds(15));

        var service = CreateSearchService(slowHandler);
        var (usedAi, results, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "面", 1, 10);

        Assert.IsFalse(usedAi, "Should NOT use AI search when Ollama does not respond within 10 seconds.");
        Assert.AreEqual(0, results.Count, "Should return empty results (caller should fall back to keyword search).");
    }

    [TestMethod]
    public async Task EmptyCache_VectorSearchSkipped()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");
        // Don't seed any embeddings — cache is empty.

        var service = CreateSearchService();
        var (usedAi, results, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "牛肉面", 1, 10);

        Assert.IsFalse(usedAi, "Should skip AI search when cache has no embeddings.");
        Assert.AreEqual(0, results.Count);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tests: search embedding database caching (issue #25)
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task EmbeddingIsCachedInDatabase_AfterSuccessfulSearch()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");
        await SeedRecipesWithEmbeddingsAsync();

        var countingHandler = new CountingOllamaHandler();
        var service = CreateSearchService(countingHandler);
        var (usedAi, _, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "牛肉面", 1, 10);

        Assert.IsTrue(usedAi);
        Assert.AreEqual(1, countingHandler.CallCount, "Ollama should be called exactly once for a new query.");

        // Verify the embedding was cached in the database.
        var cachedEntry = await _db.SearchEmbeddings
            .FirstOrDefaultAsync(e => e.QueryText == HashKey("牛肉面"));
        Assert.IsNotNull(cachedEntry, "Search embedding should be persisted to SearchEmbeddings table.");
        Assert.IsTrue(cachedEntry.Embedding.Length == VectorDimension * 4,
            "Cached embedding should be 1024 floats = 4096 bytes.");
    }

    [TestMethod]
    public async Task SecondIdenticalSearch_UsesDatabaseCache_DoesNotCallOllama()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");
        await SeedRecipesWithEmbeddingsAsync();

        var countingHandler = new CountingOllamaHandler();
        var service = CreateSearchService(countingHandler);

        // First search — Ollama must be called.
        var (usedAi1, _, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "红烧肉", 1, 10);
        Assert.IsTrue(usedAi1);
        Assert.AreEqual(1, countingHandler.CallCount);

        // Second search with same query — must use DB cache, NOT call Ollama again.
        var (usedAi2, _, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "红烧肉", 1, 10);
        Assert.IsTrue(usedAi2);
        Assert.AreEqual(1, countingHandler.CallCount,
            "Ollama should NOT be called again for a previously cached query.");

        // Different query — Ollama must be called.
        var (usedAi3, _, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "鱼香肉丝", 1, 10);
        Assert.IsTrue(usedAi3);
        Assert.AreEqual(2, countingHandler.CallCount,
            "Ollama should be called for a new, uncached query.");
    }

    [TestMethod]
    public async Task CacheTrim_LRUEviction_RemovesLeastRecentlyAccessed()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3", cacheLimit: 5);
        await SeedRecipesWithEmbeddingsAsync();

        var now = DateTime.UtcNow;

        // Seed 5 entries with staggered LastAccessedAt.
        // oldest has been accessed least recently, newest most recently.
        for (var i = 0; i < 5; i++)
        {
            _db.SearchEmbeddings.Add(new SearchEmbedding
            {
                QueryText = $"query_{i}",
                Embedding = new byte[VectorDimension * 4],
                CreatedAt = now.AddDays(-10),
                LastAccessedAt = now.AddDays(-10 + i) // 0=distant, 4=recent
            });
        }
        await _db.SaveChangesAsync();

        var countingHandler = new CountingOllamaHandler();
        var service = CreateSearchService(countingHandler);

        // A new search adds one more entry, total becomes 6 > limit 5, triggers trim.
        var (usedAi, _, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "新查询", 1, 10);
        Assert.IsTrue(usedAi);

        // The oldest entry (query_0, least recently accessed) should be evicted.
        var allQueries = await _db.SearchEmbeddings.Select(e => e.QueryText).ToListAsync();
        Assert.AreEqual(5, allQueries.Count, "Cache should be trimmed to exactly 5 entries.");
        Assert.IsFalse(allQueries.Contains("query_0"), "Least-recently-accessed entry should be evicted.");
        Assert.IsTrue(allQueries.Contains("query_4"), "Most-recently-accessed entry should survive.");
        Assert.IsTrue(allQueries.Contains("新查询"), "Newly searched query should be cached.");
    }

    [TestMethod]
    public async Task PreCachedQuery_ReturnsImmediatelyWithoutCallingOllama()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");
        await SeedRecipesWithEmbeddingsAsync();

        // Pre-cache a query embedding in the database (simulating a prior search).
        var queryVector = new float[VectorDimension];
        queryVector[0] = 0.8f;
        queryVector[1] = 0.8f;
        Normalize(queryVector);
        var bytes = new byte[VectorDimension * 4];
        Buffer.BlockCopy(queryVector, 0, bytes, 0, bytes.Length);

        _db.SearchEmbeddings.Add(new SearchEmbedding
        {
            QueryText = "预缓存查询",
            Embedding = bytes,
            CreatedAt = DateTime.UtcNow,
            LastAccessedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var countingHandler = new CountingOllamaHandler();
        var service = CreateSearchService(countingHandler);

        var (usedAi, _, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "预缓存查询", 1, 10);
        Assert.IsTrue(usedAi);
        Assert.AreEqual(0, countingHandler.CallCount,
            "Ollama should NOT be called when the query is already cached in the database.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tests: LRU cache behavior
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task CacheHit_UpdatesLastAccessedAt_WhenPastThrottle()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");
        await SeedRecipesWithEmbeddingsAsync();

        var oldDate = DateTime.UtcNow.AddHours(-2); // older than AccessThrottle (1 hour)

        // Pre-cache a query with an old LastAccessedAt.
        var queryVector = new float[VectorDimension];
        queryVector[0] = 0.8f;
        queryVector[1] = 0.8f;
        Normalize(queryVector);
        var bytes = new byte[VectorDimension * 4];
        Buffer.BlockCopy(queryVector, 0, bytes, 0, bytes.Length);

        _db.SearchEmbeddings.Add(new SearchEmbedding
        {
            QueryText = "旧查询",
            Embedding = bytes,
            CreatedAt = oldDate,
            LastAccessedAt = oldDate
        });
        await _db.SaveChangesAsync();

        var service = CreateSearchService();
        var (usedAi, _, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "旧查询", 1, 10);
        Assert.IsTrue(usedAi);

        // LastAccessedAt should have been bumped to near-now.
        var cached = await _db.SearchEmbeddings
            .AsNoTracking()
            .FirstAsync(e => e.QueryText == HashKey("旧查询"));
        Assert.IsTrue(cached.LastAccessedAt > oldDate.AddHours(1),
            "LastAccessedAt should be updated when past the access throttle window.");
    }

    [TestMethod]
    public async Task CacheHit_SkipsLastAccessedAtUpdate_WithinThrottleWindow()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");
        await SeedRecipesWithEmbeddingsAsync();

        var justNow = DateTime.UtcNow;

        var queryVector = new float[VectorDimension];
        queryVector[0] = 0.8f;
        queryVector[1] = 0.8f;
        Normalize(queryVector);
        var bytes = new byte[VectorDimension * 4];
        Buffer.BlockCopy(queryVector, 0, bytes, 0, bytes.Length);

        _db.SearchEmbeddings.Add(new SearchEmbedding
        {
            QueryText = "刚缓存",
            Embedding = bytes,
            CreatedAt = justNow,
            LastAccessedAt = justNow
        });
        await _db.SaveChangesAsync();

        var service = CreateSearchService();
        var (usedAi, _, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "刚缓存", 1, 10);
        Assert.IsTrue(usedAi);

        // LastAccessedAt should NOT have been updated — still within the throttle window.
        var cached = await _db.SearchEmbeddings
            .AsNoTracking()
            .FirstAsync(e => e.QueryText == HashKey("刚缓存"));
        Assert.AreEqual(justNow, cached.LastAccessedAt,
            "LastAccessedAt should NOT be updated when still within the access throttle window.");
    }

    [TestMethod]
    public async Task CacheLimit_RespectsConfiguredValue()
    {
        // Set limit to 2 — only 2 query embeddings should survive after a new search.
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3", cacheLimit: 2);
        await SeedRecipesWithEmbeddingsAsync();

        var countingHandler = new CountingOllamaHandler();
        var service = CreateSearchService(countingHandler);

        // Search for 3 different queries; each should be cached, but only 2 survive.
        await service.SearchAsync(_db.Recipes.AsNoTracking(), "查询A", 1, 10);
        await service.SearchAsync(_db.Recipes.AsNoTracking(), "查询B", 1, 10);
        await service.SearchAsync(_db.Recipes.AsNoTracking(), "查询C", 1, 10);

        var count = await _db.SearchEmbeddings.CountAsync();
        Assert.AreEqual(2, count, "Cache should be trimmed to exactly the configured limit of 2.");

        // The first query (least recently accessed) should be evicted.
        var existsA = await _db.SearchEmbeddings.AnyAsync(e => e.QueryText == HashKey("查询A"));
        Assert.IsFalse(existsA, "Least-recently-accessed query should be evicted when limit exceeded.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tests: multi-language embedding (Plan B)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a recipe with a Chinese embedding and an English localized embedding.
    /// The English embedding is close to the "noodle" query vector; the Chinese
    /// embedding is far from it. This simulates real data where English queries
    /// match English-localized text better than Chinese text.
    /// </summary>
    private async Task SeedMultiLanguageRecipeAsync()
    {
        // Chinese embedding — far from "noodle" query (orthogonal, dim 4-5).
        var chineseVector = EncodeVector(v => { v[4] = 1.0f; v[5] = 1.0f; });

        _db.Recipes.Add(new Recipe
        {
            Name = "牛排",
            Category = "meat_dish",
            FilePath = "dishes/meat/steak.md",
            Description = "一道经典的西式煎牛排",
            FileLastModified = DateTime.UtcNow,
            Embedding = chineseVector,
            LastEmbeddedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // English localization embedding — close to "noodle" query (dim 0-1).
        var englishVector = EncodeVector(v => { v[0] = 0.9f; v[1] = 0.9f; });

        _db.LocalizedRecipes.Add(new LocalizedRecipe
        {
            RecipeId = 1,
            Culture = "en-US",
            LocalizedName = "Steak",
            LocalizedDescription = "A classic Western-style pan-seared steak",
            Embedding = englishVector,
            LastEmbeddedAt = DateTime.UtcNow,
            LastLocalizedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await _cache.LoadAsync(_db);
    }

    [TestMethod]
    public async Task MultiEmbeddingCache_LoadsChineseAndLocalizedEmbeddings()
    {
        await SeedMultiLanguageRecipeAsync();

        var snapshot = _cache.Snapshot();
        Assert.AreEqual(1, snapshot.Count, "Should have one recipe in cache.");
        Assert.IsTrue(snapshot.ContainsKey(1));

        var embeddings = snapshot[1];
        Assert.AreEqual(2, embeddings.Count,
            "Recipe should have 2 embeddings: Chinese + en-US localization.");
    }

    [TestMethod]
    public async Task MultiEmbeddingSearch_UsesBestLanguageMatch()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");
        await SeedMultiLanguageRecipeAsync();

        // Query vector is close to English "Steak" embedding (dim 0-1).
        var fakeHandler = new FakeOllamaEmbedHandler(v =>
        {
            v[0] = 1.0f;
            v[1] = 1.0f; // near English locale
        });

        var service = CreateSearchService(fakeHandler);
        var (usedAi, results, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "steak", 1, 10);

        Assert.IsTrue(usedAi);
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("牛排", results[0].Name);
        // The English embedding (dim 0-1 close) should give a higher score
        // than the Chinese embedding (dim 4-5 far). The max should win.
    }

    [TestMethod]
    public async Task MultiEmbeddingSearch_RecipeWithOnlyChinese_StillFindsMatch()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");

        // Seed a recipe with ONLY a Chinese embedding, no localization.
        var chineseVector = EncodeVector(v => { v[0] = 0.8f; v[1] = 0.8f; });

        _db.Recipes.Add(new Recipe
        {
            Name = "汤面",
            Category = "noodle_dish",
            FilePath = "dishes/noodle/soup_noodle.md",
            Description = "汤面是一道自由搭配的家常主食",
            FileLastModified = DateTime.UtcNow,
            Embedding = chineseVector,
            LastEmbeddedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await _cache.LoadAsync(_db);

        // Query vector close to the Chinese embedding.
        var fakeHandler = new FakeOllamaEmbedHandler(v =>
        {
            v[0] = 1.0f;
            v[1] = 1.0f;
        });

        var service = CreateSearchService(fakeHandler);
        var (usedAi, results, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "noodle", 1, 10);

        Assert.IsTrue(usedAi, "AI search should be used.");
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("汤面", results[0].Name,
            "Recipe with only Chinese embedding should still be found via cross-lingual match.");
    }

    [TestMethod]
    public async Task MultiEmbeddingGetSimilar_ReturnsBestMatches()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");

        // Recipe 1: Chinese close to dim 0-1.
        var vec1 = EncodeVector(v => { v[0] = 1.0f; });
        _db.Recipes.Add(new Recipe
        {
            Name = "牛肉面",
            Category = "noodle_dish",
            FilePath = "dishes/noodle/beef_noodle.md",
            Description = "牛肉面",
            FileLastModified = DateTime.UtcNow,
            Embedding = vec1,
            LastEmbeddedAt = DateTime.UtcNow
        });

        // Recipe 2: Chinese far, but English close to dim 0-1.
        var vec2Chinese = EncodeVector(v => { v[9] = 1.0f; });
        _db.Recipes.Add(new Recipe
        {
            Name = "牛排",
            Category = "meat_dish",
            FilePath = "dishes/meat/steak.md",
            Description = "牛排",
            FileLastModified = DateTime.UtcNow,
            Embedding = vec2Chinese,
            LastEmbeddedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        // Add English localization for recipe 2, close to dim 0-1.
        var vec2English = EncodeVector(v => { v[0] = 0.9f; });
        _db.LocalizedRecipes.Add(new LocalizedRecipe
        {
            RecipeId = 2,
            Culture = "en-US",
            LocalizedName = "Steak",
            Embedding = vec2English,
            LastEmbeddedAt = DateTime.UtcNow,
            LastLocalizedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await _cache.LoadAsync(_db);

        var service = CreateSearchService();

        // Get similar recipes for recipe 2 (牛排).
        var similar = await service.GetSimilarRecipesAsync(_db.Recipes.AsNoTracking(), 2, take: 5);

        Assert.IsTrue(similar.Count > 0, "Should find at least one similar recipe.");
        Assert.AreEqual("牛肉面", similar[0].Name,
            "Steak's English embedding should match beef noodle's Chinese embedding.");
    }

    [TestMethod]
    public async Task MultiEmbeddingSearch_ChineseQuery_StillWorks()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");

        // Recipe: Chinese embedding close to query.
        var chineseVector = EncodeVector(v => { v[0] = 0.9f; v[1] = 0.9f; });
        _db.Recipes.Add(new Recipe
        {
            Name = "汤面",
            Category = "noodle_dish",
            FilePath = "dishes/noodle/soup_noodle.md",
            Description = "面条软滑",
            FileLastModified = DateTime.UtcNow,
            Embedding = chineseVector,
            LastEmbeddedAt = DateTime.UtcNow
        });

        // English translation — far from the Chinese-query vector.
        var englishVector = EncodeVector(v => { v[1] = 1.0f; v[9] = 1.0f; });
        _db.LocalizedRecipes.Add(new LocalizedRecipe
        {
            RecipeId = 1,
            Culture = "en-US",
            LocalizedName = "Noodle Soup",
            Embedding = englishVector,
            LastEmbeddedAt = DateTime.UtcNow,
            LastLocalizedAt = DateTime.UtcNow
        });

        // Non-noodle recipe — Chinese embedding far.
        var otherVector = EncodeVector(v => { v[9] = 1.0f; });
        _db.Recipes.Add(new Recipe
        {
            Name = "牛排",
            Category = "meat_dish",
            FilePath = "dishes/meat/steak.md",
            Description = "牛排",
            FileLastModified = DateTime.UtcNow,
            Embedding = otherVector,
            LastEmbeddedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();
        await _cache.LoadAsync(_db);

        // Query vector close to dim 0 — matches Chinese "汤面" embedding.
        var fakeHandler = new FakeOllamaEmbedHandler(v =>
        {
            v[0] = 1.0f;
            // NOT dim 1 (English), NOT dim 9 (other)
        });

        var service = CreateSearchService(fakeHandler);
        var (usedAi, results, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "面条", 1, 10);

        Assert.IsTrue(usedAi);
        Assert.IsTrue(results.Count > 0);
        // Chinese "汤面" should outrank "牛排" even though the English locale embedding
        // has a different orientation — the Chinese embedding should win for Chinese queries.
        var noodleSoupIndex = results.FindIndex(r => r.Name == "汤面");
        var steakIndex = results.FindIndex(r => r.Name == "牛排");
        Assert.IsTrue(noodleSoupIndex >= 0, "汤面 should be in results.");
        Assert.IsTrue(noodleSoupIndex < steakIndex || steakIndex < 0,
            "汤面 (noodle soup) should rank above 牛排 (steak) for query '面条'.");
    }

    [TestMethod]
    public async Task MultiEmbeddingCache_LoadsOnlyValidVectors()
    {
        // Recipe with malformed embedding byte length (not multiple of 4).
        _db.Recipes.Add(new Recipe
        {
            Name = "坏数据",
            Category = "test",
            FilePath = "test/bad.md",
            Description = "bad",
            FileLastModified = DateTime.UtcNow,
            Embedding = [1, 2, 3], // 3 bytes — not divisible by 4
            LastEmbeddedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        // Localized recipe with valid embedding.
        var validVector = EncodeVector(v => { v[0] = 1.0f; });
        _db.LocalizedRecipes.Add(new LocalizedRecipe
        {
            RecipeId = 1,
            Culture = "en-US",
            LocalizedName = "Test",
            Embedding = validVector,
            LastEmbeddedAt = DateTime.UtcNow,
            LastLocalizedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        await _cache.LoadAsync(_db);

        var snapshot = _cache.Snapshot();
        // The malformed Chinese embedding should be skipped, but the valid
        // English localization should still load.
        Assert.IsTrue(snapshot.ContainsKey(1),
            "Recipe with valid localization embedding should be in cache.");
        Assert.AreEqual(1, snapshot[1].Count,
            "Only the valid localization embedding should load; malformed recipe embedding skipped.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test HTTP message handlers
    // ─────────────────────────────────────────────────────────────────────────

    private class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private class FakeOllamaEmbedHandler : HttpMessageHandler
    {
        private readonly Action<float[]>? _initVector;

        /// <param name="initVector">
        /// Optional: custom initializer for the returned 1024-dim vector.
        /// Defaults to the noodle-query vector (dims 0,1 = 1.0).
        /// </param>
        public FakeOllamaEmbedHandler(Action<float[]>? initVector = null)
        {
            _initVector = initVector;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var vector = new float[VectorDimension];
            if (_initVector != null)
            {
                _initVector(vector);
            }
            else
            {
                vector[0] = 1.0f;
                vector[1] = 1.0f;
            }

            var response = new
            {
                embeddings = new[] { vector }
            };

            var json = JsonConvert.SerializeObject(response);
            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(httpResponse);
        }
    }

    private class SlowOllamaHandler : HttpMessageHandler
    {
        private readonly TimeSpan _delay;

        public SlowOllamaHandler(TimeSpan delay) => _delay = delay;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            try
            {
                await Task.Delay(_delay, ct);
            }
            catch (OperationCanceledException)
            {
                // The caller's timeout CTS cancelled us — that's the expected path.
            }

            // If we reach here, return OK (the timeout already fired upstream).
            var response = new { embeddings = new[] { new float[VectorDimension] } };
            var json = JsonConvert.SerializeObject(response);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    /// <summary>
    /// Fake Ollama handler that counts how many times it was invoked.
    /// Used to verify that repeated queries hit the database cache instead of calling Ollama.
    /// </summary>
    private class CountingOllamaHandler : HttpMessageHandler
    {
        public int CallCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref CallCount);

            var vector = new float[VectorDimension];
            vector[0] = 1.0f;
            vector[1] = 1.0f;

            var response = new
            {
                embeddings = new[] { vector }
            };

            var json = JsonConvert.SerializeObject(response);
            var httpResponse = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(httpResponse);
        }
    }
}
