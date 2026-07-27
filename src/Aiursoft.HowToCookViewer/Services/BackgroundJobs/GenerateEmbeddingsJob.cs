using System.Text;
using Aiursoft.Canon;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Util;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

/// <summary>
/// Generates embedding vectors for recipes using the configured Ollama embedding model.
/// Processes recipes whose content has changed (FileLastModified) since the
/// last embedding was generated. Also processes localized recipe translations
/// whose LastLocalizedAt is newer than LastEmbeddedAt.
/// </summary>
public class GenerateEmbeddingsJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    IHttpClientFactory httpClientFactory,
    RetryEngine retryEngine,
    ILogger<GenerateEmbeddingsJob> logger) : IBackgroundJob
{
    internal const int MaxDocumentsPerRun = 50;
    private static readonly SemaphoreSlim RunLock = new(1, 1);

    public string Name => "Generate Recipe Embeddings";

    public string Description =>
        "Generates 1024-dimension embedding vectors for recipes using the configured Ollama embedding model.";

    public async Task ExecuteAsync()
    {
        if (!await RunLock.WaitAsync(0))
        {
            logger.LogInformation("GenerateEmbeddingsJob: previous run is still active. Skipping.");
            return;
        }

        try
        {
            await ExecuteCoreAsync();
        }
        finally
        {
            RunLock.Release();
        }
    }

    private async Task ExecuteCoreAsync()
    {
        if (!await settingsService.IsAiSearchEnabledAsync())
        {
            logger.LogInformation("GenerateEmbeddingsJob: Ollama endpoint not configured. Skipping.");
            return;
        }

        var useAiSearch = await settingsService.GetBoolSettingAsync(SettingsMap.EnableEmbeddingBasedSearch);
        if (!useAiSearch)
        {
            logger.LogInformation("GenerateEmbeddingsJob: EnableEmbeddingBasedSearch is disabled. Skipping.");
            return;
        }

        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        if (string.IsNullOrWhiteSpace(model))
        {
            logger.LogInformation("GenerateEmbeddingsJob: EmbeddingModel not configured. Skipping.");
            return;
        }

        var instance = await settingsService.GetEmbeddingEndpointAsync();
        var token = await settingsService.GetEmbeddingTokenAsync();

        var attempted = 0;
        var succeeded = 0;

        // Phase 1: Generate embeddings for Chinese recipes.
        var lastId = 0;
        while (true)
        {
            if (attempted >= MaxDocumentsPerRun)
            {
                logger.LogInformation(
                    "GenerateEmbeddingsJob: attempted {Count} documents, stopping until next run.",
                    attempted);
                break;
            }

            var currentLastId = lastId;
            var take = Math.Min(10, MaxDocumentsPerRun - attempted);
            var pendingRecipes = await db.Recipes
                .Where(r => r.Id > currentLastId && r.LastEmbeddedAt < r.FileLastModified)
                .OrderBy(r => r.Id)
                .Take(take)
                .ToListAsync();

            if (pendingRecipes.Count == 0) break;

            foreach (var recipe in pendingRecipes)
            {
                attempted++;
                try
                {
                    var sourceFileLastModified = recipe.FileLastModified;
                    var embedding = await retryEngine.RunWithRetry(async _ =>
                    {
                        var text = BuildRecipeText(recipe);
                        return await CallEmbedApiAsync(instance, model, token, text, recipe.Name);
                    });

                    var serialized = EmbeddingHelper.Serialize(embedding);
                    if (await TrySaveEmbeddingIfRecipeUnchangedAsync(db, recipe, sourceFileLastModified, serialized))
                    {
                        succeeded++;
                    }
                    else
                    {
                        logger.LogInformation(
                            "GenerateEmbeddingsJob: recipe '{Name}' (id={Id}) changed while embedding was running. Skipping stale result.",
                            recipe.Name, recipe.Id);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "GenerateEmbeddingsJob: Failed to generate embedding for recipe '{Name}'.", recipe.Name);
                }
            }

            lastId = pendingRecipes.Max(r => r.Id);
        }

        // Phase 2: Generate embeddings for localized recipe translations.
        lastId = 0;
        while (true)
        {
            if (attempted >= MaxDocumentsPerRun)
            {
                logger.LogInformation(
                    "GenerateEmbeddingsJob: attempted {Count} documents, stopping until next run.",
                    attempted);
                break;
            }

            var currentLastId = lastId;
            var take = Math.Min(10, MaxDocumentsPerRun - attempted);
            var pending = await db.LocalizedRecipes
                .Where(lr => lr.Id > currentLastId && lr.LastEmbeddedAt < lr.LastLocalizedAt)
                .OrderBy(lr => lr.Id)
                .Take(take)
                .ToListAsync();

            if (pending.Count == 0) break;

            foreach (var loc in pending)
            {
                attempted++;
                try
                {
                    var sourceLastLocalizedAt = loc.LastLocalizedAt;
                    var text = BuildLocalizedRecipeText(loc);
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        if (await TrySaveEmbeddingIfLocalizedRecipeUnchangedAsync(db, loc, sourceLastLocalizedAt, null))
                        {
                            succeeded++;
                        }

                        continue;
                    }

                    var embedding = await retryEngine.RunWithRetry(async _ =>
                    {
                        return await CallEmbedApiAsync(instance, model, token, text,
                            $"{loc.LocalizedName} ({loc.Culture})");
                    });

                    var serialized = EmbeddingHelper.Serialize(embedding);
                    if (await TrySaveEmbeddingIfLocalizedRecipeUnchangedAsync(db, loc, sourceLastLocalizedAt, serialized))
                    {
                        succeeded++;
                    }
                    else
                    {
                        logger.LogInformation(
                            "GenerateEmbeddingsJob: localized recipe '{Name}' ({Culture}) changed while embedding was running. Skipping stale result.",
                            loc.LocalizedName, loc.Culture);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "GenerateEmbeddingsJob: Failed to generate localized embedding for '{Name}' ({Culture}).",
                        loc.LocalizedName, loc.Culture);
                }
            }

            lastId = pending.Max(lr => lr.Id);
        }

        logger.LogInformation(
            "GenerateEmbeddingsJob: done. {Succeeded}/{Attempted} documents processed.",
            succeeded, attempted);
    }

    internal async Task<float[]> CallEmbedApiAsync(string instance, string model, string token, string text, string logName)
    {
        var http = httpClientFactory.CreateClient();

        var baseUri = new Uri(instance);
        var embedEndpoint = $"{baseUri.Scheme}://{baseUri.Authority}/api/embed?keep_alive=-1";

        // bge-m3 has an 8192-token context window. Characters map to tokens at different
        // rates per language (CJK ≈ 1:1, English ≈ 1:4). Start with 8000 chars (safe for
        // all languages) and use binary-search fallback if Ollama still reports the input
        // is too long.
        var maxChars = 8000;
        while (true)
        {
            var input = TruncateForEmbedding(text, maxChars);

            var requestBody = new { model, input };
            var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, embedEndpoint) { Content = content };
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            }

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var response = await http.SendAsync(request, timeoutCts.Token);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>();
                if (result?.Embeddings == null || result.Embeddings.Length == 0)
                {
                    throw new Exception($"Ollama returned no embeddings for '{logName}'.");
                }

                var vector = result.Embeddings[0];
                EmbeddingHelper.Normalize(vector);
                return vector;
            }

            // If the input is too long, halve the limit and retry. Otherwise fail.
            var errorBody = await response.Content.ReadAsStringAsync();
            var isContextError = errorBody.Contains("context", StringComparison.OrdinalIgnoreCase) ||
                                 errorBody.Contains("length", StringComparison.OrdinalIgnoreCase) ||
                                 errorBody.Contains("exceed", StringComparison.OrdinalIgnoreCase);
            if (!isContextError || maxChars <= 500)
            {
                throw new HttpRequestException(
                    $"Ollama embedding request failed for '{logName}' (HTTP {(int)response.StatusCode}): {errorBody}");
            }

            var prev = maxChars;
            maxChars /= 2;
            logger.LogWarning(
                "Embedding input for '{Name}' still too long at {Prev} chars, retrying with {Current} chars (binary fallback).",
                logName, prev, maxChars);
        }
    }

    /// <summary>
    /// Truncates text to fit within bge-m3's 8192-token context window.
    /// Uses head+tail preservation: keeps the first 75% and last ~25% of the budget
    /// so both the introduction and conclusion contribute to the embedding.
    /// </summary>
    internal static string TruncateForEmbedding(string text, int maxChars)
    {
        if (text.Length <= maxChars) return text;

        var head = (int)(maxChars * 0.75);
        var tail = maxChars - head - 5; // 5 for "\n...\n" separator
        if (tail <= 0) return text[..maxChars];

        return string.Concat(text.AsSpan(0, head), "\n...\n", text.AsSpan(text.Length - tail));
    }

    private static string BuildRecipeText(Recipe recipe)
    {
        // Concatenate the most semantically meaningful fields for embedding.
        var sb = new StringBuilder();
        sb.AppendLine(recipe.Name);
        if (!string.IsNullOrWhiteSpace(recipe.Description))
            sb.AppendLine(recipe.Description);
        if (!string.IsNullOrWhiteSpace(recipe.Ingredients))
            sb.AppendLine(recipe.Ingredients);
        return sb.ToString();
    }

    private static string BuildLocalizedRecipeText(LocalizedRecipe loc)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(loc.LocalizedName))
            sb.AppendLine(loc.LocalizedName);
        if (!string.IsNullOrWhiteSpace(loc.LocalizedDescription))
            sb.AppendLine(loc.LocalizedDescription);
        if (!string.IsNullOrWhiteSpace(loc.LocalizedIngredients))
            sb.AppendLine(loc.LocalizedIngredients);
        return sb.ToString();
    }

    internal static async Task<bool> TrySaveEmbeddingIfRecipeUnchangedAsync(
        TemplateDbContext db,
        Recipe recipe,
        DateTime sourceFileLastModified,
        byte[]? embedding)
    {
        if (db.Database.IsRelational())
        {
            var updated = await db.Recipes
                .Where(r => r.Id == recipe.Id && r.FileLastModified == sourceFileLastModified)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.Embedding, embedding)
                    .SetProperty(r => r.LastEmbeddedAt, sourceFileLastModified));
            return updated == 1;
        }

        await db.Entry(recipe).ReloadAsync();
        if (db.Entry(recipe).State == EntityState.Detached || recipe.FileLastModified != sourceFileLastModified)
        {
            return false;
        }

        recipe.Embedding = embedding;
        recipe.LastEmbeddedAt = sourceFileLastModified;
        await db.SaveChangesAsync();
        return true;
    }

    internal static async Task<bool> TrySaveEmbeddingIfLocalizedRecipeUnchangedAsync(
        TemplateDbContext db,
        LocalizedRecipe loc,
        DateTime sourceLastLocalizedAt,
        byte[]? embedding)
    {
        if (db.Database.IsRelational())
        {
            var updated = await db.LocalizedRecipes
                .Where(lr => lr.Id == loc.Id && lr.LastLocalizedAt == sourceLastLocalizedAt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(lr => lr.Embedding, embedding)
                    .SetProperty(lr => lr.LastEmbeddedAt, sourceLastLocalizedAt));
            return updated == 1;
        }

        await db.Entry(loc).ReloadAsync();
        if (db.Entry(loc).State == EntityState.Detached || loc.LastLocalizedAt != sourceLastLocalizedAt)
        {
            return false;
        }

        loc.Embedding = embedding;
        loc.LastEmbeddedAt = sourceLastLocalizedAt;
        await db.SaveChangesAsync();
        return true;
    }

    private class OllamaEmbedResponse
    {
        [JsonProperty("embeddings")]
        public float[][]? Embeddings { get; set; }
    }
}
