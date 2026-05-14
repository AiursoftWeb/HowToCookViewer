using System.Text;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.HowToCookViewer.Services;

/// <summary>
/// Semantic vector search for recipes using an Ollama-hosted embedding model (bge-m3).
/// Computes cosine similarity against an in-memory cache of pre-computed recipe embeddings.
/// Falls back to classic keyword search when AI search is unavailable or times out.
/// </summary>
public class RecipeVectorSearchService(
    RecipeEmbeddingCache cache,
    GlobalSettingsService settingsService,
    IHttpClientFactory httpClientFactory)
{
    private const int EmbedTimeoutSeconds = 10;

    public async Task<(bool UsedAi, List<Recipe> Results, int TotalCount)> SearchAsync(
        IQueryable<Recipe> baseQuery,
        string query,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        // Check all three preconditions for AI vector search.
        if (!await ShouldAttemptVectorSearch())
        {
            return (false, [], 0);
        }

        var snapshot = cache.Snapshot();
        if (snapshot.Count == 0)
        {
            return (false, [], 0);
        }

        float[]? queryVector;
        try
        {
            queryVector = await EmbedQueryAsync(query, ct);
        }
        catch (Exception)
        {
            return (false, [], 0);
        }

        if (queryVector == null)
        {
            return (false, [], 0);
        }

        // Compute cosine similarity for all cached recipes, rank, and paginate.
        var scored = snapshot
            .Select(kv => (RecipeId: kv.Key, Score: CosineSimilarity(queryVector, kv.Value)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ToList();

        var total = scored.Count;
        var topIds = scored
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => x.RecipeId)
            .ToList();

        if (topIds.Count == 0)
        {
            return (true, [], total);
        }

        // Load full recipe objects from DB, preserving vector-score order.
        var recipes = await baseQuery
            .Include(r => r.Images)
            .Where(r => topIds.Contains(r.Id))
            .ToListAsync(ct);

        var recipeMap = recipes.ToDictionary(r => r.Id);
        var ordered = topIds
            .Select(id => recipeMap.GetValueOrDefault(id))
            .Where(r => r != null)
            .Cast<Recipe>()
            .ToList();

        return (true, ordered, total);
    }

    private async Task<bool> ShouldAttemptVectorSearch()
    {
        var useAi = await settingsService.GetBoolSettingAsync(SettingsMap.UseAiSearch);
        if (!useAi) return false;

        var instance = await settingsService.GetSettingValueAsync(SettingsMap.OllamaInstance);
        if (string.IsNullOrWhiteSpace(instance)) return false;

        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        if (string.IsNullOrWhiteSpace(model)) return false;

        return true;
    }

    private async Task<float[]?> EmbedQueryAsync(string text, CancellationToken ct)
    {
        var instance = await settingsService.GetSettingValueAsync(SettingsMap.OllamaInstance);
        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        var token = await settingsService.GetSettingValueAsync(SettingsMap.OllamaToken);

        var http = httpClientFactory.CreateClient();
        var baseUri = new Uri(instance);
        var embedEndpoint = $"{baseUri.Scheme}://{baseUri.Authority}/api/embed?keep_alive=-1";
        // num_gpu=0: CPU-only inference avoids VRAM contention with the LLM.
        var requestBody = new { model, input = text, options = new { num_gpu = 0 } };
        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, embedEndpoint) { Content = content };
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(EmbedTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        var response = await http.SendAsync(request, linkedCts.Token);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<OllamaEmbedResponse>(linkedCts.Token);
        if (result?.Embeddings == null || result.Embeddings.Length == 0)
        {
            return null;
        }

        var vector = result.Embeddings[0];
        Normalize(vector);
        return vector;
    }

    /// <summary>Cosine similarity between two normalized vectors = dot product.</summary>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        var dot = 0f;
        for (var i = 0; i < a.Length; i++)
            dot += a[i] * b[i];
        return dot;
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

    private class OllamaEmbedResponse
    {
        [JsonProperty("embeddings")]
        public float[][]? Embeddings { get; set; }
    }
}
