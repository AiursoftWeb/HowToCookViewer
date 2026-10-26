using System.Net;
using System.Text.Json;
using Aiursoft.Canon;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.InMemory;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Services.BackgroundJobs;
using Aiursoft.WebTools;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class EmbeddingRetryTests
{
    private InMemoryContext _db = null!;
    private MemoryCache _memory = null!;
    private GlobalSettingsService _settings = null!;
    private RecipeEmbeddingCache _cache = null!;
    private RecipeVectorSearchService _service = null!;
    private Recipe _item = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _db = new InMemoryContext(new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _memory = new MemoryCache(new MemoryCacheOptions());
        _settings = new GlobalSettingsService(_db, new ConfigurationBuilder().Build(), null!, _memory,
            new EphemeralDataProtectionProvider(), NullLogger<GlobalSettingsService>.Instance);
        _db.GlobalSettings.AddRange(
            new GlobalSetting { Key = SettingsMap.EnableEmbeddingBasedSearch, Value = "True" },
            new GlobalSetting { Key = SettingsMap.EmbeddingOllamaInstance, Value = "https://embedding.example.com" },
            new GlobalSetting { Key = SettingsMap.EmbeddingModel, Value = "test-embedding-model" });
        _item = new Recipe
        {
            Name = "Matching recipe", Description = new string('a', 10000), Category = "test", FilePath = "test/recipe.md", FileLastModified = DateTime.UtcNow,
            Embedding = EmbeddingHelper.Serialize([1f, 0f])
        };
        _db.Recipes.Add(_item);
        await _db.SaveChangesAsync();
        _cache = new RecipeEmbeddingCache(NullLogger<RecipeEmbeddingCache>.Instance);
        await _cache.LoadAsync(_db);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _db.Dispose();
        _memory.Dispose();
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest)]
    [DataRow(HttpStatusCode.RequestEntityTooLarge)]
    public async Task QueryShrinksContextErrorsAndCachesOnlyTheSuccessfulVector(HttpStatusCode status)
    {
        using var handler = new EmbeddingHandler { ErrorStatus = status };
        _service = new RecipeVectorSearchService(_db, _cache, _settings, handler, NullLogger<RecipeVectorSearchService>.Instance);
        var query = "Introduction " + new string('a', 10000) + " Conclusion";

        var result = await _service.SearchAsync(_db.Recipes.AsNoTracking(), query, 1, 10);

        Assert.IsTrue(result.UsedAi);
        Assert.AreEqual(1, result.TotalCount);
        CollectionAssert.AreEqual(new[] { 8000, 4000, 2000 }, handler.Inputs.Select(input => input.Length).ToArray());
        foreach (var input in handler.Inputs) Assert.AreEqual(query[..input.Length], input);
        Assert.AreEqual(1, await _db.SearchEmbeddings.CountAsync());
        await _service.SearchAsync(_db.Recipes.AsNoTracking(), query, 1, 10);
        Assert.AreEqual(3, handler.Inputs.Count, "Successful query embeddings must be reused.");
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest, "Request body length exceeds the limit")]
    [DataRow(HttpStatusCode.RequestEntityTooLarge, "Request body length exceeds the limit")]
    [DataRow(HttpStatusCode.RequestEntityTooLarge, "Context service unavailable")]
    [DataRow(HttpStatusCode.InternalServerError, "context length exceeded")]
    public async Task UnrelatedQueryErrorsFallBackWithoutShrinking(HttpStatusCode status, string error)
    {
        using var handler = new EmbeddingHandler { ErrorStatus = status, ErrorMessage = error, ContextLimit = 0 };
        _service = new RecipeVectorSearchService(_db, _cache, _settings, handler, NullLogger<RecipeVectorSearchService>.Instance);
        var query = new string('a', 10000);

        var result = await _service.SearchAsync(_db.Recipes.AsNoTracking(), query, 1, 10);

        Assert.IsFalse(result.UsedAi);
        Assert.AreEqual(1, handler.Inputs.Count);
        Assert.AreEqual(8000, handler.Inputs[0].Length);
        Assert.AreEqual(0, await _db.SearchEmbeddings.CountAsync());
    }

    [TestMethod]
    public async Task RepeatedContextErrorsStopAtTheMinimumBudget()
    {
        using var handler = new EmbeddingHandler { ContextLimit = 0 };
        _service = new RecipeVectorSearchService(_db, _cache, _settings, handler, NullLogger<RecipeVectorSearchService>.Instance);
        var query = new string('a', 10000);

        var result = await _service.SearchAsync(_db.Recipes.AsNoTracking(), query, 1, 10);

        Assert.IsFalse(result.UsedAi);
        CollectionAssert.AreEqual(new[] { 8000, 4000, 2000, 1000, 500 }, handler.Inputs.Select(input => input.Length).ToArray());
        Assert.AreEqual(0, await _db.SearchEmbeddings.CountAsync());
    }

    [TestMethod]
    [DataRow(HttpStatusCode.BadRequest, "Request body length exceeds the limit")]
    [DataRow(HttpStatusCode.RequestEntityTooLarge, "Request body length exceeds the limit")]
    [DataRow(HttpStatusCode.InternalServerError, "context length exceeded")]
    public async Task BackgroundErrorsNeverRetryWithUnrelatedTruncation(HttpStatusCode status, string error)
    {
        _item.Embedding = null;
        await _db.SaveChangesAsync();
        using var handler = new EmbeddingHandler { ErrorStatus = status, ErrorMessage = error, ContextLimit = 0 };
        var retry = new RetryEngine(NullLogger<RetryEngine>.Instance);
        var job = new GenerateEmbeddingsJob(_db, _settings, handler, retry, NullLogger<GenerateEmbeddingsJob>.Instance);

        await job.ExecuteAsync();

        Assert.IsTrue(handler.Inputs.Count > 0);
        Assert.IsTrue(handler.Inputs.All(input => input.Length == 8000), "Transient job retries must not shrink unrelated failures.");
        Assert.IsNull(_item.Embedding);
    }

    private sealed class EmbeddingHandler : HttpMessageHandler, IHttpClientFactory
    {
        public List<string> Inputs { get; } = [];
        public HttpStatusCode ErrorStatus { get; init; } = HttpStatusCode.RequestEntityTooLarge;
        public string ErrorMessage { get; init; } = "{\"error\":\"the input length exceeds the context length\"}";
        public int ContextLimit { get; init; } = 2000;

        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.AreEqual("/api/embed", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Assert.AreEqual("test-embedding-model", body.RootElement.GetProperty("model").GetString());
            var input = body.RootElement.GetProperty("input").GetString()!;
            Inputs.Add(input);
            return input.Length > ContextLimit
                ? new HttpResponseMessage(ErrorStatus) { Content = new StringContent(ErrorMessage) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { embeddings = new[] { new[] { 1f, 0f } } }) };
        }
    }
}
