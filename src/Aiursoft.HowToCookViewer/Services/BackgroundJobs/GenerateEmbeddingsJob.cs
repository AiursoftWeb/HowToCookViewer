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
        if (!await settingsService.IsAiFeatureEnabledAsync())
        {
            logger.LogInformation("GenerateEmbeddingsJob: Ollama endpoint not configured. Skipping.");
            return;
        }

        var useAiSearch = await settingsService.GetBoolSettingAsync(SettingsMap.UseAiSearch);
        if (!useAiSearch)
        {
            logger.LogInformation("GenerateEmbeddingsJob: UseAiSearch is disabled. Skipping.");
            return;
        }

        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        if (string.IsNullOrWhiteSpace(model))
        {
            logger.LogInformation("GenerateEmbeddingsJob: EmbeddingModel not configured. Skipping.");
            return;
        }

        var instance = await settingsService.GetSettingValueAsync(SettingsMap.OllamaInstance);
        var token = await settingsService.GetSettingValueAsync(SettingsMap.OllamaToken);

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
        // num_gpu=0 forces CPU-only inference so the embedding model never competes with the LLM for VRAM.
        var requestBody = new { model, input = text, options = new { num_gpu = 0 } };
        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, embedEndpoint) { Content = content };
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var response = await http.SendAsync(request, timeoutCts.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>();
        if (result?.Embeddings == null || result.Embeddings.Length == 0)
        {
            throw new Exception($"Ollama returned no embeddings for recipe '{recipe.Name}'.");
        }

        var vector = result.Embeddings[0];
        Normalize(vector);
        return vector;
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
