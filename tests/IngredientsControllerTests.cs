using System.Net;
using System.Text;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Controllers;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.InMemory;
using Aiursoft.HowToCookViewer.Models.IngredientsViewModels;
using Aiursoft.HowToCookViewer.Services;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class IngredientsControllerTests
{
    private const int VectorDim = 1024;

    private InMemoryContext _db = null!;
    private IngredientGroupService _groupService = null!;
    private GlobalSettingsService _settingsService = null!;
    private IngredientsController _controller = null!;

    [TestInitialize]
    public void Initialize()
    {
        var dbName = "IngredientsCtrlTest_" + Guid.NewGuid();
        var dbOptions = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        _db = new InMemoryContext(dbOptions);

        var config = new ConfigurationBuilder().Build();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        _settingsService = new GlobalSettingsService(_db, config, null!, memoryCache);

        var httpFactory = new TestHttpClientFactory(new FakeOllamaEmbedHandler());
        var logger = NullLogger<IngredientGroupService>.Instance;
        _groupService = new IngredientGroupService(httpFactory, logger);

        var httpContextAccessor = new FakeHttpContextAccessor();
        var localizationService = new RecipeLocalizationService(_db, httpContextAccessor);

        var stringLocalizer = new FakeStringLocalizer<IngredientsController>();

        _controller = new IngredientsController(
            _db, localizationService, stringLocalizer, _groupService, _settingsService);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        _controller.Dispose();
    }

    // ─────────────────────────────────────────────────────────────────
    // Lookup: empty input
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Lookup_NullIds_ReturnsEmptyPartial()
    {
        var result = await _controller.Lookup(null);
        var partialView = result as PartialViewResult;
        Assert.IsNotNull(partialView);
        var model = partialView.Model as LookupResultsViewModel;
        Assert.IsNotNull(model);
        Assert.IsTrue(model.ExactMatches.Recipes.Count == 0);
        Assert.IsTrue(model.NearMatches.Count == 0);
    }

    [TestMethod]
    public async Task Lookup_EmptyIds_ReturnsEmptyPartial()
    {
        var result = await _controller.Lookup([]);
        var partialView = result as PartialViewResult;
        Assert.IsNotNull(partialView);
        var model = partialView.Model as LookupResultsViewModel;
        Assert.IsNotNull(model);
        Assert.IsTrue(model.ExactMatches.Recipes.Count == 0);
        Assert.IsTrue(model.NearMatches.Count == 0);
    }

    // ─────────────────────────────────────────────────────────────────
    // Lookup: basic matching
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Lookup_SingleMatchingIngredient_ReturnsRecipe()
    {
        var salt = new Ingredient { Name = "盐" };
        _db.Ingredients.Add(salt);

        var recipe = new Recipe
        {
            Name = "炒青菜", Category = "dish",
            FilePath = "dishes/stir_fry_greens.md",
            Description = "", FileLastModified = DateTime.UtcNow,
        };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        // Link ingredient to recipe
        recipe.ConsumedIngredients.Add(salt);
        await _db.SaveChangesAsync();

        var result = await _controller.Lookup([salt.Id]);
        var partialView = result as PartialViewResult;
        Assert.IsNotNull(partialView);
        var model = partialView.Model as LookupResultsViewModel;
        Assert.IsNotNull(model);
        Assert.AreEqual(1, model.ExactMatches.Recipes.Count,
            "Recipe with only the matching ingredient should be an exact match.");
        Assert.AreEqual("炒青菜", model.ExactMatches.Recipes[0].Name);
    }

    // ─────────────────────────────────────────────────────────────────
    // Lookup: ID expansion via group
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Lookup_ExpandsCanonicalId_ToFindAliasRecipe()
    {
        // Two ingredients with identical embeddings at threshold 80 → same group.
        // Canonical = shortest name ("盐" shorter than "食用盐").
        var vector = EncodeVector(v => { v[0] = 1f; });
        var salt = new Ingredient { Name = "盐", Embedding = vector.ToArray() };
        var tableSalt = new Ingredient { Name = "食用盐", Embedding = vector.ToArray() };
        _db.Ingredients.AddRange(salt, tableSalt);

        var recipe = new Recipe
        {
            Name = "炒青菜", Category = "dish",
            FilePath = "dishes/stir_fry_greens.md",
            Description = "", FileLastModified = DateTime.UtcNow,
        };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        recipe.ConsumedIngredients.Add(tableSalt);
        await _db.SaveChangesAsync();
        await SeedSettingsAsync(threshold: "80");

        var groups = await _groupService.GetGroupsAsync(_db, _settingsService);
        Assert.AreEqual(1, groups.Count, "Two ingredients with identical embeddings should merge.");
        var canonicalId = groups[0].Canonical.Id;
        Assert.AreEqual("盐", groups[0].Canonical.Name,
            "Shortest name should be canonical.");

        // ExpandCanonicalIds should include both canonical and alias
        var expanded = _groupService.ExpandCanonicalIds([canonicalId]);
        Assert.IsTrue(expanded.Contains(tableSalt.Id),
            $"Expanded IDs {string.Join(",", expanded)} should include alias tableSalt.Id={tableSalt.Id}");

        // Look up by canonical ID → should find recipe through alias expansion
        var result = await _controller.Lookup([canonicalId]);
        var partialView = result as PartialViewResult;
        Assert.IsNotNull(partialView);
        var model = partialView.Model as LookupResultsViewModel;
        Assert.IsNotNull(model);

        Assert.AreEqual(1, model.ExactMatches.Recipes.Count);
        Assert.AreEqual("炒青菜", model.ExactMatches.Recipes[0].Name);
    }

    [TestMethod]
    public async Task Lookup_WithoutGroupExpansion_OriginalIdWorks()
    {
        var salt = new Ingredient { Name = "盐" };
        _db.Ingredients.Add(salt);

        var recipe = new Recipe
        {
            Name = "炒青菜", Category = "dish",
            FilePath = "dishes/stir_fry_greens.md",
            Description = "", FileLastModified = DateTime.UtcNow,
        };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        recipe.ConsumedIngredients.Add(salt);
        await _db.SaveChangesAsync();

        var result = await _controller.Lookup([salt.Id]);
        var partialView = result as PartialViewResult;
        Assert.IsNotNull(partialView);
        var model = partialView.Model as LookupResultsViewModel;
        Assert.IsNotNull(model);
        Assert.IsTrue(model.ExactMatches.Recipes.Any(r => r.Name == "炒青菜"));
    }

    // ─────────────────────────────────────────────────────────────────
    // Lookup: partial match / near match
    // ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task Lookup_PartialMatch_ReturnsNearMatch()
    {
        var salt = new Ingredient { Name = "盐" };
        var sugar = new Ingredient { Name = "糖" };
        _db.Ingredients.AddRange(salt, sugar);

        var recipe = new Recipe
        {
            Name = "糖醋排骨", Category = "dish",
            FilePath = "dishes/sweet_sour_ribs.md",
            Description = "", FileLastModified = DateTime.UtcNow,
        };
        _db.Recipes.Add(recipe);
        await _db.SaveChangesAsync();

        recipe.ConsumedIngredients.Add(salt);
        recipe.ConsumedIngredients.Add(sugar);
        await _db.SaveChangesAsync();

        // Query only "盐" — recipe needs both salt and sugar, so 50% match (< 60%)
        var result = await _controller.Lookup([salt.Id]);
        var partialView = result as PartialViewResult;
        Assert.IsNotNull(partialView);
        var model = partialView.Model as LookupResultsViewModel;
        Assert.IsNotNull(model);
        Assert.IsTrue(model.ExactMatches.Recipes.Count == 0,
            "50% match should not be in exact matches.");
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────

    private async Task SeedSettingsAsync(string threshold = "80")
    {
        var defaults = new Dictionary<string, string>
        {
            [SettingsMap.EmbeddingModel] = "bge-m3:latest",
            [SettingsMap.EmbeddingOllamaInstance] = "http://localhost:11434",
            [SettingsMap.EmbeddingApiToken] = "",
            [SettingsMap.IngredientSimilarityThreshold] = threshold
        };

        foreach (var (key, value) in defaults)
        {
            if (!await _db.GlobalSettings.AnyAsync(s => s.Key == key))
                _db.GlobalSettings.Add(new GlobalSetting { Key = key, Value = value });
        }
        await _db.SaveChangesAsync();
    }

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

    // ─────────────────────────────────────────────────────────────────
    // Fakes
    // ─────────────────────────────────────────────────────────────────

    private class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler);
    }

    private class FakeOllamaEmbedHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            var req = JsonConvert.DeserializeAnonymousType(body, new { input = (object?)null });
            var count = req?.input switch
            {
                System.Collections.IEnumerable array => array.Cast<object>().Count(),
                _ => 1
            };

            var embeddings = new float[count][];
            for (var i = 0; i < count; i++)
            {
                var vector = new float[VectorDim];
                vector[0] = 1f;
                vector[1] = 1f;
                Normalize(vector);
                embeddings[i] = vector;
            }

            var response = new { embeddings };
            var json = JsonConvert.SerializeObject(response);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }

    private class FakeHttpContextAccessor : IHttpContextAccessor
    {
        public HttpContext? HttpContext
        {
            get
            {
                var context = new DefaultHttpContext();
                context.Features.Set<IRequestCultureFeature>(
                    new RequestCultureFeature(
                        new RequestCulture("zh-CN"),
                        null!));
                return context;
            }
            set { }
        }
    }

    private class FakeStringLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
