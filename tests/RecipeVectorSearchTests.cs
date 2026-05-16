using System.Net;
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
        _cache = new RecipeEmbeddingCache();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
    }

    private async Task SeedGlobalSettingsAsync(
        bool useAiSearch = false,
        string ollamaInstance = "",
        string embeddingModel = "")
    {
        var settings = new[]
        {
            new GlobalSetting { Key = SettingsMap.UseAiSearch, Value = useAiSearch ? "True" : "False" },
            new GlobalSetting { Key = SettingsMap.OllamaInstance, Value = ollamaInstance },
            new GlobalSetting { Key = SettingsMap.OllamaModel, Value = "" },
            new GlobalSetting { Key = SettingsMap.OllamaToken, Value = "" },
            new GlobalSetting { Key = SettingsMap.EmbeddingModel, Value = embeddingModel }
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

    private RecipeVectorSearchService CreateSearchService(HttpMessageHandler? handler = null)
    {
        var settings = CreateSettingsService();
        var httpClientFactory = handler != null
            ? new TestHttpClientFactory(handler)
            : (IHttpClientFactory)new TestHttpClientFactory(new FakeOllamaEmbedHandler());
        return new RecipeVectorSearchService(_db, _cache, settings, httpClientFactory);
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
        var (usedAi, results, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "牛肉面", 1, 10);

        Assert.IsTrue(usedAi);
        Assert.AreEqual(1, countingHandler.CallCount, "Ollama should be called exactly once for a new query.");

        // Verify the embedding was cached in the database.
        var cachedEntry = await _db.SearchEmbeddings
            .FirstOrDefaultAsync(e => e.QueryText == "牛肉面");
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
        var (usedAi1, results1, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "红烧肉", 1, 10);
        Assert.IsTrue(usedAi1);
        Assert.AreEqual(1, countingHandler.CallCount);

        // Second search with same query — must use DB cache, NOT call Ollama again.
        var (usedAi2, results2, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "红烧肉", 1, 10);
        Assert.IsTrue(usedAi2);
        Assert.AreEqual(1, countingHandler.CallCount,
            "Ollama should NOT be called again for a previously cached query.");

        // Different query — Ollama must be called.
        var (usedAi3, results3, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "鱼香肉丝", 1, 10);
        Assert.IsTrue(usedAi3);
        Assert.AreEqual(2, countingHandler.CallCount,
            "Ollama should be called for a new, uncached query.");
    }

    [TestMethod]
    public async Task CacheTrim_OldestEntriesRemovedWhenExceedingLimit()
    {
        await SeedGlobalSettingsAsync(useAiSearch: true, ollamaInstance: "http://localhost:11434", embeddingModel: "bge-m3");
        await SeedRecipesWithEmbeddingsAsync();

        // Pre-seed the cache with MaxCachedQueries entries.
        var oldDate = DateTime.UtcNow.AddDays(-10);
        for (var i = 0; i < 10; i++)
        {
            _db.SearchEmbeddings.Add(new SearchEmbedding
            {
                QueryText = $"old_query_{i}",
                Embedding = new byte[VectorDimension * 4],
                CreatedAt = oldDate
            });
        }
        await _db.SaveChangesAsync();

        var countingHandler = new CountingOllamaHandler();
        var service = CreateSearchService(countingHandler);

        // A new search should succeed and trigger trimming (10 old + 1 new > limit unlikely).
        var (usedAi, _, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "新查询", 1, 10);
        Assert.IsTrue(usedAi);

        // The new query should be in the cache.
        var newEntry = await _db.SearchEmbeddings
            .FirstOrDefaultAsync(e => e.QueryText == "新查询");
        Assert.IsNotNull(newEntry, "Newly searched query should be cached.");
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
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var countingHandler = new CountingOllamaHandler();
        var service = CreateSearchService(countingHandler);

        var (usedAi, results, _) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "预缓存查询", 1, 10);
        Assert.IsTrue(usedAi);
        Assert.AreEqual(0, countingHandler.CallCount,
            "Ollama should NOT be called when the query is already cached in the database.");
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
