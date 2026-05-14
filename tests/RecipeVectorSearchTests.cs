using System.Net;
using System.Text;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.InMemory;
using Aiursoft.HowToCookViewer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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
        // Simulated query vector for "面" (noodles): first two dims high.
        var noodleVector = EncodeVector(v => v[0] = v[1] = 1.0f);

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
        return new RecipeVectorSearchService(_cache, settings, httpClientFactory);
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
        var (usedAi, results, total) = await service.SearchAsync(_db.Recipes.AsNoTracking(), "面", 1, 10);

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
}
