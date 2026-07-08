using System.Text;
using Aiursoft.Canon;
using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

/// <summary>
/// Generates embedding vectors for recipes using the configured Ollama embedding model.
/// Processes recipes whose FileLastModified is newer than LastEmbeddedAt.
/// </summary>
public class GenerateEmbeddingsJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    IHttpClientFactory httpClientFactory,
    RetryEngine retryEngine,
    ILogger<GenerateEmbeddingsJob> logger) : IBackgroundJob
{
    public string Name => "Generate Recipe Embeddings";

    public string Description =>
        "Generates 1024-dimension embedding vectors for recipes using the configured Ollama embedding model.";

    public async Task ExecuteAsync()
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

        var instance = await GetEmbeddingInstanceAsync();
        var token = await GetEmbeddingTokenAsync();

        var lastId = 0;
        while (true)
        {
            var currentLastId = lastId;
            var pendingRecipes = await db.Recipes
                .Where(r => r.Id > currentLastId && r.LastEmbeddedAt < r.FileLastModified)
                .OrderBy(r => r.Id)
                .Take(10)
                .ToListAsync();

            if (pendingRecipes.Count == 0) break;

            foreach (var recipe in pendingRecipes)
            {
                try
                {
                    await retryEngine.RunWithRetry(async _ =>
                    {
                        var embedding = await CallEmbedApiAsync(instance, model, token, recipe);
                        recipe.Embedding = Serialize(embedding);
                        recipe.LastEmbeddedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync();
                    });
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "GenerateEmbeddingsJob: Failed to generate embedding for recipe '{Name}'.", recipe.Name);
                }
            }

            lastId = pendingRecipes.Max(r => r.Id);
        }
    }

    private async Task<float[]> CallEmbedApiAsync(string instance, string model, string token, Recipe recipe)
    {
        var text = BuildRecipeText(recipe);
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

            // num_gpu=0 forces CPU-only inference so the embedding model never competes with the LLM for VRAM.
            var requestBody = new { model, input, options = new { num_gpu = 0 } };
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
                    throw new Exception($"Ollama returned no embeddings for recipe '{recipe.Name}'.");
                }

                var vector = result.Embeddings[0];
                Normalize(vector);
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
                    $"Ollama embedding request failed for '{recipe.Name}' (HTTP {(int)response.StatusCode}): {errorBody}");
            }

            var prev = maxChars;
            maxChars /= 2;
            logger.LogWarning(
                "Embedding input for '{Name}' still too long at {Prev} chars, retrying with {Current} chars (binary fallback).",
                recipe.Name, prev, maxChars);
        }
    }

    private async Task<string> GetEmbeddingInstanceAsync()
    {
        var dedicated = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingOllamaInstance);
        if (!string.IsNullOrWhiteSpace(dedicated)) return dedicated;

        return await settingsService.GetSettingValueAsync(SettingsMap.OpenAiInstance);
    }

    private async Task<string> GetEmbeddingTokenAsync()
    {
        var dedicated = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingApiToken);
        if (!string.IsNullOrWhiteSpace(dedicated)) return dedicated;

        return await settingsService.GetSettingValueAsync(SettingsMap.OpenAiApiToken);
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

    private static byte[] Serialize(float[] vector)
    {
        var bytes = new byte[vector.Length * 4];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private class OllamaEmbedResponse
    {
        [JsonProperty("embeddings")]
        public float[][]? Embeddings { get; set; }
    }
}
