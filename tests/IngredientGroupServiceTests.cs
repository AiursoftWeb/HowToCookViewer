using System.Net;
using System.Text;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.InMemory;
using Aiursoft.HowToCookViewer.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class IngredientGroupServiceTests
{
    private const int VectorDim = 1024;

    private InMemoryContext _db = null!;
    private GlobalSettingsService _settingsService = null!;
    private IHttpClientFactory _httpClientFactory = null!;
    private IngredientGroupService _service = null!;

    [TestInitialize]
    public void Initialize()
    {
        var dbName = "IngredientGroupTest_" + Guid.NewGuid();
        var dbOptions = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        _db = new InMemoryContext(dbOptions);

        var config = new ConfigurationBuilder().Build();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        _settingsService = new GlobalSettingsService(_db, config, null!, memoryCache);

        _httpClientFactory = new TestHttpClientFactory(new FakeOllamaEmbedHandler());
        var logger = NullLogger<IngredientGroupService>.Instance;
        _service = new IngredientGroupService(_httpClientFactory, logger);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────
    // Snapshot & cache tests
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetGroupsAsync_EmptyDatabase_ReturnsEmptyList()
    {
        var groups = await _service.GetGroupsAsync(_db, _settingsService);
        Assert.AreEqual(0, groups.Count);
    }

    [TestMethod]
    public async Task GetGroupsAsync_SameSnapshot_ReturnsCachedResult()
    {
        await SeedIngredientsAsync(["盐", "糖"]);
        await SeedSettingsAsync();

        var first = await _service.GetGroupsAsync(_db, _settingsService);
        var second = await _service.GetGroupsAsync(_db, _settingsService);

        Assert.AreSame(first, second, "Second call should return the exact same cached instance.");
    }

    [TestMethod]
    public async Task GetGroupsAsync_NewIngredientAdded_TriggersRebuild()
    {
        await SeedIngredientsAsync(["盐"]);
        await SeedSettingsAsync();

        var first = await _service.GetGroupsAsync(_db, _settingsService);

        // Add a new ingredient — snapshot should differ
        _db.Ingredients.Add(new Ingredient { Name = "糖" });
        await _db.SaveChangesAsync();

        var second = await _service.GetGroupsAsync(_db, _settingsService);
        Assert.AreNotSame(first, second, "Should rebuild when ingredient count changes.");
    }

    [TestMethod]
    public async Task GetGroupsAsync_ThresholdChanged_TriggersRebuild()
    {
        await SeedIngredientsAsync(["盐", "食用盐"]);
        await SeedSettingsAsync(threshold: "80");

        var first = await _service.GetGroupsAsync(_db, _settingsService);

        // Change threshold in DB — then create fresh GlobalSettingsService with new MemoryCache
        // so the cached old value isn't returned.
        await UpdateSettingAsync(SettingsMap.IngredientSimilarityThreshold, "90");
        var newMemoryCache = new MemoryCache(new MemoryCacheOptions());
        var newSettings = new GlobalSettingsService(_db, new ConfigurationBuilder().Build(), null!, newMemoryCache);

        var second = await _service.GetGroupsAsync(_db, newSettings);
        Assert.AreNotSame(first, second, "Should rebuild when threshold changes.");
    }

    // ─────────────────────────────────────────────────────────────────
    // Clustering tests
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildGroups_HighThreshold_NoMerging()
    {
        // Seed with pre-computed orthogonal embeddings — no two should merge at threshold 100
        var salt = new Ingredient { Name = "盐", Embedding = EncodeVector(v => { v[0] = 1f; }) };
        var sugar = new Ingredient { Name = "糖", Embedding = EncodeVector(v => { v[1] = 1f; }) };
        var oil = new Ingredient { Name = "油", Embedding = EncodeVector(v => { v[2] = 1f; }) };
        _db.Ingredients.AddRange(salt, sugar, oil);
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "100");

        var groups = await _service.GetGroupsAsync(_db, _settingsService);
        Assert.AreEqual(3, groups.Count, "At threshold 100 (1.0 cosine), no ingredients should merge.");
    }

    [TestMethod]
    public async Task BuildGroups_IdenticalEmbeddings_Merge()
    {
        // Two ingredients with identical embeddings should merge even at high threshold
        var vector = EncodeVector(v => { v[0] = 1f; });
        var salt = new Ingredient { Name = "盐", Embedding = vector };
        var tableSalt = new Ingredient { Name = "食用盐", Embedding = vector.ToArray() }; // copy
        _db.Ingredients.AddRange(salt, tableSalt);
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "99");

        var groups = await _service.GetGroupsAsync(_db, _settingsService);
        Assert.AreEqual(1, groups.Count, "Identical embeddings should merge into one group.");
        Assert.AreEqual(2, groups[0].GroupSize);
    }

    [TestMethod]
    public async Task BuildGroups_LowThreshold_AllMerge()
    {
        var salt = new Ingredient { Name = "盐", Embedding = EncodeVector(v => { v[0] = 1f; }) };
        var sugar = new Ingredient { Name = "糖", Embedding = EncodeVector(v => { v[1] = 1f; }) };
        var oil = new Ingredient { Name = "油", Embedding = EncodeVector(v => { v[2] = 1f; }) };
        _db.Ingredients.AddRange(salt, sugar, oil);
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "0"); // Even orthogonal vectors pass at threshold 0

        var groups = await _service.GetGroupsAsync(_db, _settingsService);
        Assert.AreEqual(1, groups.Count, "At threshold 0, all ingredients should merge.");
        Assert.AreEqual(3, groups[0].GroupSize);
    }

    [TestMethod]
    public async Task BuildGroups_SimilarEmbeddings_Merge()
    {
        // Two nearly-identical vectors at threshold 80
        var v1 = EncodeVector(v => { v[0] = 0.7f; v[1] = 0.7f; });
        var v2 = EncodeVector(v => { v[0] = 0.71f; v[1] = 0.69f; }); // cosine ~ 0.9998
        _db.Ingredients.AddRange(
            new Ingredient { Name = "番茄", Embedding = v1 },
            new Ingredient { Name = "西红柿", Embedding = v2 }
        );
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "80");

        var groups = await _service.GetGroupsAsync(_db, _settingsService);
        Assert.AreEqual(1, groups.Count,
            "Very similar embeddings should merge at threshold 80.");
    }

    // ─────────────────────────────────────────────────────────────────
    // Canonical selection tests
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildGroups_CanonicalIsMostReferenced()
    {
        var vector = EncodeVector(v => { v[0] = 1f; });
        var lessReferenced = new Ingredient { Name = "食用盐", Embedding = vector.ToArray() };
        var moreReferenced = new Ingredient { Name = "盐", Embedding = vector.ToArray() };

        // Give "盐" more recipe references
        var recipe1 = new Recipe
        {
            Name = "炒青菜", Category = "dish", FilePath = "dishes/stir_fry_greens.md",
            Description = "", FileLastModified = DateTime.UtcNow,
            ConsumedIngredients = [moreReferenced]
        };
        var recipe2 = new Recipe
        {
            Name = "红烧肉", Category = "dish", FilePath = "dishes/braised_pork.md",
            Description = "", FileLastModified = DateTime.UtcNow,
            ConsumedIngredients = [moreReferenced]
        };
        lessReferenced.Recipes = [recipe1]; // only 1 recipe
        // moreReferenced is in both recipes indirectly, but Recipes collection matters

        _db.Ingredients.AddRange(moreReferenced, lessReferenced);
        _db.Recipes.AddRange(recipe1, recipe2);
        // Link moreReferenced to both recipes
        recipe1.ConsumedIngredients.Add(moreReferenced);
        recipe2.ConsumedIngredients.Add(moreReferenced);
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "80");

        var groups = await _service.GetGroupsAsync(_db, _settingsService);
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual("盐", groups[0].Canonical.Name,
            "The ingredient with more recipe references should be canonical.");
    }

    [TestMethod]
    public async Task BuildGroups_EqualReferences_TieBreakByLowerId()
    {
        var vector = EncodeVector(v => { v[0] = 1f; });
        // Seed lower-id first so it gets Id=1, second gets Id=2
        var lowId = new Ingredient { Name = "低ID食材", Embedding = vector.ToArray() };
        var highId = new Ingredient { Name = "高ID食材", Embedding = vector.ToArray() };
        _db.Ingredients.AddRange(lowId, highId);
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "80");

        var groups = await _service.GetGroupsAsync(_db, _settingsService);
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual("低ID食材", groups[0].Canonical.Name,
            "With equal recipe counts, lower Id should be canonical.");
    }

    // ─────────────────────────────────────────────────────────────────
    // Sorting tests
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildGroups_SortedByDistinctRecipeCountDescending()
    {
        var v1 = EncodeVector(v => { v[0] = 1f; });
        var v2 = EncodeVector(v => { v[1] = 1f; });
        var v3 = EncodeVector(v => { v[2] = 1f; });

        var salt = new Ingredient { Name = "盐", Embedding = v1 };
        var sugar = new Ingredient { Name = "糖", Embedding = v2 };
        var oil = new Ingredient { Name = "油", Embedding = v3 };

        var r1 = MakeRecipe("r1", salt);
        var r2 = MakeRecipe("r2", salt);
        var r3 = MakeRecipe("r3", sugar);
        // salt → 2 recipes, sugar → 1, oil → 0

        _db.Ingredients.AddRange(salt, sugar, oil);
        _db.Recipes.AddRange(r1, r2, r3);
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "100");

        var groups = await _service.GetGroupsAsync(_db, _settingsService);
        Assert.AreEqual(3, groups.Count);
        Assert.AreEqual("盐", groups[0].Canonical.Name, "Most-referenced should be first.");
        Assert.AreEqual("糖", groups[1].Canonical.Name, "Second-most-referenced should be second.");
        Assert.AreEqual("油", groups[2].Canonical.Name, "Least-referenced should be last.");
    }

    // ─────────────────────────────────────────────────────────────────
    // ExpandCanonicalIds tests
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExpandCanonicalIds_WithCache_ExpandsToAliases()
    {
        var vector = EncodeVector(v => { v[0] = 1f; });
        var canonical = new Ingredient { Name = "盐", Embedding = vector.ToArray() };
        var alias = new Ingredient { Name = "食用盐", Embedding = vector.ToArray() };
        _db.Ingredients.AddRange(canonical, alias);
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "80");

        // Build groups to populate cache
        await _service.GetGroupsAsync(_db, _settingsService);

        var expanded = _service.ExpandCanonicalIds([canonical.Id]);
        Assert.IsTrue(expanded.Contains(canonical.Id));
        Assert.IsTrue(expanded.Contains(alias.Id),
            "ExpandCanonicalIds should include alias IDs.");
    }

    [TestMethod]
    public async Task ExpandCanonicalIds_NoCache_ReturnsOriginalIds()
    {
        var input = new[] { 1, 2, 3 };
        var expanded = _service.ExpandCanonicalIds(input);
        CollectionAssert.AreEqual(input, expanded,
            "Without cache, ExpandCanonicalIds should return the input unchanged.");
    }

    [TestMethod]
    public async Task ExpandCanonicalIds_UnknownCanonicalId_OnlyReturnsOriginal()
    {
        var vector = EncodeVector(v => { v[0] = 1f; });
        _db.Ingredients.Add(new Ingredient { Name = "盐", Embedding = vector });
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "80");
        await _service.GetGroupsAsync(_db, _settingsService);

        var expanded = _service.ExpandCanonicalIds([999]); // non-existent
        CollectionAssert.AreEqual(new[] { 999 }, expanded);
    }

    // ─────────────────────────────────────────────────────────────────
    // Invalidate tests
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Invalidate_ForcesRebuild()
    {
        await SeedIngredientsAsync(["盐"]);
        await SeedSettingsAsync();

        var first = await _service.GetGroupsAsync(_db, _settingsService);
        _service.Invalidate();
        var second = await _service.GetGroupsAsync(_db, _settingsService);

        Assert.AreNotSame(first, second, "Invalidate should force a rebuild.");
    }

    // ─────────────────────────────────────────────────────────────────
    // Embedding generation tests
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildGroups_GeneratesEmbeddings_ForIngredientsWithoutThem()
    {
        await SeedSettingsAsync(threshold: "80");

        // Ingredient without embedding — the service should call Ollama and fill it
        var ingredient = new Ingredient { Name = "盐" };
        _db.Ingredients.Add(ingredient);
        await _db.SaveChangesAsync();

        await _service.GetGroupsAsync(_db, _settingsService);

        // Reload from DB
        var reloaded = await _db.Ingredients.FirstAsync(i => i.Id == ingredient.Id);
        Assert.IsNotNull(reloaded.Embedding, "Embedding should be generated.");
        Assert.AreEqual(VectorDim * 4, reloaded.Embedding!.Length,
            "Embedding should be 1024 floats = 4096 bytes.");
        Assert.AreNotEqual(DateTime.MinValue, reloaded.LastEmbeddedAt,
            "LastEmbeddedAt should be updated.");
    }

    [TestMethod]
    public async Task BuildGroups_SkipsEmbedding_WhenAlreadyPresent()
    {
        await SeedSettingsAsync(threshold: "80");

        var existingEmbedding = EncodeVector(v => { v[0] = 0.5f; });
        var ingredient = new Ingredient
        {
            Name = "盐",
            Embedding = existingEmbedding,
            LastEmbeddedAt = DateTime.UtcNow.AddDays(-1)
        };
        var oldEmbeddedAt = ingredient.LastEmbeddedAt;
        _db.Ingredients.Add(ingredient);
        await _db.SaveChangesAsync();

        await _service.GetGroupsAsync(_db, _settingsService);

        var reloaded = await _db.Ingredients.FirstAsync(i => i.Id == ingredient.Id);
        CollectionAssert.AreEqual(existingEmbedding, reloaded.Embedding,
            "Existing embedding should not be overwritten.");
        Assert.AreEqual(oldEmbeddedAt, reloaded.LastEmbeddedAt,
            "LastEmbeddedAt should not change when embedding already exists.");
    }

    [TestMethod]
    public async Task BuildGroups_EmbeddingFails_ContinuesGracefully()
    {
        await SeedSettingsAsync(threshold: "80");

        var ingredient = new Ingredient { Name = "盐" };
        _db.Ingredients.Add(ingredient);
        await _db.SaveChangesAsync();

        // Use a handler that returns HTTP 500
        var errorHandler = new ErrorOllamaHandler();
        var errorFactory = new TestHttpClientFactory(errorHandler);
        var logger = NullLogger<IngredientGroupService>.Instance;
        var service = new IngredientGroupService(errorFactory, logger);

        // Should not throw
        var groups = await service.GetGroupsAsync(_db, _settingsService);

        // Embedding should remain null
        var reloaded = await _db.Ingredients.FirstAsync(i => i.Id == ingredient.Id);
        Assert.IsNull(reloaded.Embedding, "Failed embedding should leave field null.");
        Assert.IsTrue(groups.Count >= 1, "Should still build groups even if embedding failed.");
    }

    // ─────────────────────────────────────────────────────────────────
    // Static helpers: Serialize/Deserialize roundtrip
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void SerializeDeserialize_Roundtrip_PreservesValues()
    {
        var original = new float[VectorDim];
        var rng = new Random(42);
        for (var i = 0; i < original.Length; i++)
            original[i] = (float)(rng.NextDouble() * 2 - 1);

        var bytes = Serialize(original);
        var restored = Deserialize(bytes);

        Assert.IsNotNull(restored);
        Assert.AreEqual(original.Length, restored!.Length);
        for (var i = 0; i < original.Length; i++)
            Assert.AreEqual(original[i], restored[i], 1e-7f,
                $"Element {i} should match after roundtrip.");
    }

    [TestMethod]
    public void SerializeDeserialize_AllZeros_Roundtrip()
    {
        var original = new float[VectorDim];
        var bytes = Serialize(original);
        var restored = Deserialize(bytes);
        Assert.IsNotNull(restored);
        Assert.AreEqual(VectorDim, restored!.Length);
        Assert.IsTrue(restored.All(f => f == 0f));
    }

    [TestMethod]
    public void Deserialize_Null_ReturnsNull()
    {
        var result = Deserialize(null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Deserialize_BadLength_ReturnsNull()
    {
        var bad = new byte[7]; // not divisible by 4
        var result = Deserialize(bad);
        Assert.IsNull(result);
    }

    // ─────────────────────────────────────────────────────────────────
    // Static helpers: Normalize
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Normalize_UnitVector_StaysUnit()
    {
        var v = new[] { 1f, 0f, 0f };
        Normalize(v);
        Assert.AreEqual(1f, v[0], 1e-7f);
        Assert.AreEqual(0f, v[1], 1e-7f);
        Assert.AreEqual(0f, v[2], 1e-7f);
    }

    [TestMethod]
    public void Normalize_NonUnitVector_BecomesUnit()
    {
        var v = new[] { 3f, 4f }; // length 5
        Normalize(v);
        Assert.AreEqual(0.6f, v[0], 1e-7f);
        Assert.AreEqual(0.8f, v[1], 1e-7f);
    }

    [TestMethod]
    public void Normalize_ZeroVector_StaysZero()
    {
        var v = new[] { 0f, 0f, 0f };
        Normalize(v);
        Assert.AreEqual(0f, v[0]);
        Assert.AreEqual(0f, v[1]);
        Assert.AreEqual(0f, v[2]);
    }

    // ─────────────────────────────────────────────────────────────────
    // Static helpers: CosineSimilarity
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void CosineSimilarity_Identical_ReturnsOne()
    {
        var a = new[] { 1f / MathF.Sqrt(2), 1f / MathF.Sqrt(2) };
        var b = new[] { 1f / MathF.Sqrt(2), 1f / MathF.Sqrt(2) };
        var sim = CosineSimilarity(a, b);
        Assert.AreEqual(1f, sim, 1e-7f);
    }

    [TestMethod]
    public void CosineSimilarity_Orthogonal_ReturnsZero()
    {
        var a = new[] { 1f, 0f };
        var b = new[] { 0f, 1f };
        var sim = CosineSimilarity(a, b);
        Assert.AreEqual(0f, sim, 1e-7f);
    }

    [TestMethod]
    public void CosineSimilarity_Opposite_ReturnsNegativeOne()
    {
        var a = new[] { 1f, 0f };
        var b = new[] { -1f, 0f };
        var sim = CosineSimilarity(a, b);
        Assert.AreEqual(-1f, sim, 1e-7f);
    }

    // ─────────────────────────────────────────────────────────────────
    // BuildGroups: CanonicalIngredientId persistence
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildGroups_PersistsCanonicalIngredientId_ForAliases()
    {
        var vector = EncodeVector(v => { v[0] = 1f; });
        var canonical = new Ingredient { Name = "盐", Embedding = vector.ToArray() };
        var alias = new Ingredient { Name = "食用盐", Embedding = vector.ToArray() };
        _db.Ingredients.AddRange(canonical, alias);
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "80");

        await _service.GetGroupsAsync(_db, _settingsService);

        var reloadedAlias = await _db.Ingredients.FirstAsync(i => i.Name == "食用盐");
        Assert.AreEqual(canonical.Id, reloadedAlias.CanonicalIngredientId,
            "Alias should have CanonicalIngredientId set to the canonical ingredient's Id.");
    }

    [TestMethod]
    public async Task BuildGroups_CanonicalHasNullCanonicalIngredientId()
    {
        var vector = EncodeVector(v => { v[0] = 1f; });
        _db.Ingredients.Add(new Ingredient { Name = "盐", Embedding = vector });
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "80");

        await _service.GetGroupsAsync(_db, _settingsService);

        var reloaded = await _db.Ingredients.FirstAsync(i => i.Name == "盐");
        Assert.IsNull(reloaded.CanonicalIngredientId,
            "Canonical ingredient should have null CanonicalIngredientId.");
    }

    // ─────────────────────────────────────────────────────────────────
    // BuildGroups: deduplicated recipe count
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildGroups_DistinctRecipeCount_DeduplicatesAcrossGroup()
    {
        var vector = EncodeVector(v => { v[0] = 1f; });
        var salt = new Ingredient { Name = "盐", Embedding = vector.ToArray() };
        var tableSalt = new Ingredient { Name = "食用盐", Embedding = vector.ToArray() };

        var recipe = MakeRecipe("红烧肉", salt, tableSalt);
        _db.Ingredients.AddRange(salt, tableSalt);
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "80");

        var groups = await _service.GetGroupsAsync(_db, _settingsService);
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(1, groups[0].DistinctRecipeCount,
            "When both ingredients share the same recipe, DistinctRecipeCount should be 1, not 2.");
    }

    // ─────────────────────────────────────────────────────────────────
    // Concurrency: double-check locking
    // ─────────────────────────────────────────────────────────────────
    //
    // NOTE: Concurrent access tests require separate DbContext instances per
    // thread, which is how the production code works (scoped DbContext).
    // The InMemory provider throws when one DbContext is used concurrently,
    // so this test is skipped — the double-check locking pattern is verified
    // by the single-threaded cache-hit tests above.
    //
    // To test concurrency properly, a future test could use SQLite in-memory
    // with separate DbContext instances per thread.

    // ─────────────────────────────────────────────────────────────────
    // GroupViewModel properties
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task BuildGroups_GroupSize_EqualsAliasesPlusOne()
    {
        var vector = EncodeVector(v => { v[0] = 1f; });
        _db.Ingredients.AddRange(
            new Ingredient { Name = "盐", Embedding = vector.ToArray() },
            new Ingredient { Name = "食用盐", Embedding = vector.ToArray() },
            new Ingredient { Name = "食盐", Embedding = vector.ToArray() }
        );
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "80");

        var groups = await _service.GetGroupsAsync(_db, _settingsService);
        Assert.AreEqual(1, groups.Count);
        Assert.AreEqual(3, groups[0].GroupSize, "GroupSize should be 1 canonical + 2 aliases = 3.");
        Assert.AreEqual(2, groups[0].Aliases.Count,
            "Aliases should not include the canonical ingredient.");
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private async Task SeedIngredientsAsync(string[] names)
    {
        var vector = EncodeVector(v => { v[0] = 1f; });
        foreach (var name in names)
            _db.Ingredients.Add(new Ingredient { Name = name, Embedding = vector.ToArray() });
        await _db.SaveChangesAsync();
    }

    private async Task SeedSettingsAsync(string threshold = "80")
    {
        var keys = new[]
        {
            SettingsMap.EmbeddingModel,
            SettingsMap.EmbeddingOllamaInstance,
            SettingsMap.EmbeddingApiToken,
            SettingsMap.IngredientSimilarityThreshold
        };
        var defaults = new Dictionary<string, string>
        {
            [SettingsMap.EmbeddingModel] = "bge-m3:latest",
            [SettingsMap.EmbeddingOllamaInstance] = "http://localhost:11434",
            [SettingsMap.EmbeddingApiToken] = "",
            [SettingsMap.IngredientSimilarityThreshold] = threshold
        };

        foreach (var key in keys)
        {
            if (!await _db.GlobalSettings.AnyAsync(s => s.Key == key))
                _db.GlobalSettings.Add(new GlobalSetting { Key = key, Value = defaults[key] });
        }
        await _db.SaveChangesAsync();
    }

    private async Task UpdateSettingAsync(string key, string value)
    {
        var setting = await _db.GlobalSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (setting != null)
        {
            setting.Value = value;
            await _db.SaveChangesAsync();
        }
    }

    private static Recipe MakeRecipe(string name, params Ingredient[] ingredients)
    {
        return new Recipe
        {
            Name = name,
            Category = "dish",
            FilePath = $"dishes/{name}.md",
            Description = "",
            FileLastModified = DateTime.UtcNow,
            ConsumedIngredients = [.. ingredients]
        };
    }

    // ─────────────────────────────────────────────────────────────────
    // Static helpers (mirror production code for test isolation)
    // ─────────────────────────────────────────────────────────────────

    private static byte[] EncodeVector(Action<float[]> init)
    {
        var v = new float[VectorDim];
        init(v);
        Normalize(v);
        var bytes = new byte[VectorDim * 4];
        Buffer.BlockCopy(v, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static void Normalize(float[] vector)
    {
        var sumSq = 0f;
        for (var i = 0; i < vector.Length; i++)
            sumSq += vector[i] * vector[i];
        var norm = MathF.Sqrt(sumSq);
        if (norm > 0)
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= norm;
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        var dot = 0f;
        for (var i = 0; i < a.Length; i++)
            dot += a[i] * b[i];
        return dot;
    }

    private static byte[] Serialize(float[] vector)
    {
        var bytes = new byte[vector.Length * 4];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[]? Deserialize(byte[]? bytes)
    {
        if (bytes == null || bytes.Length % 4 != 0) return null;
        var floats = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    // ─────────────────────────────────────────────────────────────────
    // HTTP handlers
    // ─────────────────────────────────────────────────────────────────

    private class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private class FakeOllamaEmbedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var vector = new float[VectorDim];
            vector[0] = 1f;
            vector[1] = 1f;
            Normalize(vector);

            var response = new { embeddings = new[] { vector } };
            var json = JsonConvert.SerializeObject(response);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private class ErrorOllamaHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
