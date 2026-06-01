using System.Text;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Util;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Aiursoft.HowToCookViewer.Services;

/// <summary>
/// Semantic vector search for recipes using an Ollama-hosted embedding model (bge-m3).
/// Computes cosine similarity against an in-memory cache of pre-computed recipe embeddings.
/// Caches query embeddings in the database (circular buffer of 2000 entries) to avoid
/// redundant calls to the embedding model.
/// Falls back to classic keyword search when AI search is unavailable or times out.
/// </summary>
public class RecipeVectorSearchService(
    TemplateDbContext db,
    RecipeEmbeddingCache cache,
    GlobalSettingsService settingsService,
    IHttpClientFactory httpClientFactory)
{
    private const int EmbedTimeoutSeconds = 10;

    /// <summary>
    /// Only update LastAccessedAt when the previous update was at least this long ago.
    /// Avoids a write on every cache-hit search while still preserving approximate LRU order.
    /// </summary>
    internal static readonly TimeSpan AccessThrottle = TimeSpan.FromHours(1);

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
            .Select(kv => (RecipeId: kv.Key, Score: EmbeddingHelper.CosineSimilarity(queryVector, kv.Value)))
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

    public async Task<List<Recipe>> GetSimilarRecipesAsync(
        IQueryable<Recipe> baseQuery,
        int recipeId,
        int take,
        CancellationToken ct = default)
    {
        var snapshot = cache.Snapshot();
        if (!snapshot.TryGetValue(recipeId, out var targetVector))
        {
            return [];
        }

        var topIds = snapshot
            .Where(kv => kv.Key != recipeId)
            .Select(kv => (RecipeId: kv.Key, Score: EmbeddingHelper.CosineSimilarity(targetVector, kv.Value)))
            .OrderByDescending(x => x.Score)
            .Take(take)
            .Select(x => x.RecipeId)
            .ToList();

        if (topIds.Count == 0)
        {
            return [];
        }

        var recipes = await baseQuery
            .Include(r => r.Images)
            .Where(r => topIds.Contains(r.Id))
            .ToListAsync(ct);

        var recipeMap = recipes.ToDictionary(r => r.Id);
        return topIds
            .Select(id => recipeMap.GetValueOrDefault(id))
            .Where(r => r != null)
            .Cast<Recipe>()
            .ToList();
    }

    private async Task<bool> ShouldAttemptVectorSearch()
    {
        var useAi = await settingsService.GetBoolSettingAsync(SettingsMap.EnableEmbeddingBasedSearch);
        if (!useAi) return false;

        var instance = await GetEmbeddingInstanceAsync();
        if (string.IsNullOrWhiteSpace(instance)) return false;

        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        if (string.IsNullOrWhiteSpace(model)) return false;

        return true;
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

    private async Task<float[]?> EmbedQueryAsync(string text, CancellationToken ct)
    {
        // Check database cache first.
        var cached = await db.SearchEmbeddings
            .FirstOrDefaultAsync(e => e.QueryText == text, ct);

        if (cached != null)
        {
            var vector = EmbeddingHelper.Deserialize(cached.Embedding);
            if (vector != null)
            {
                // Throttled LRU bump: only touch the timestamp every AccessThrottle.
                var now = DateTime.UtcNow;
                if (now - cached.LastAccessedAt >= AccessThrottle)
                {
                    cached.LastAccessedAt = now;
                    await db.SaveChangesAsync(ct);
                }

                return vector;
            }
        }

        // Compute embedding via Ollama.
        var instance = await GetEmbeddingInstanceAsync();
        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        var token = await GetEmbeddingTokenAsync();

        var http = httpClientFactory.CreateClient();
        var baseUri = new Uri(instance);
        var embedEndpoint = $"{baseUri.Scheme}://{baseUri.Authority}/api/embed?keep_alive=-1";
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

        var embedding = result.Embeddings[0];
        EmbeddingHelper.Normalize(embedding);

        // Cache the result in the database.
        var serialized = EmbeddingHelper.Serialize(embedding);
        try
        {
            var now = DateTime.UtcNow;
            db.SearchEmbeddings.Add(new SearchEmbedding
            {
                QueryText = text,
                Embedding = serialized,
                CreatedAt = now,
                LastAccessedAt = now
            });
            await db.SaveChangesAsync(ct);

            // Trim to MaxCachedQueries entries (circular buffer).
            await TrimCacheAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Race: another request already cached this query. Ignore.
        }

        return embedding;
    }

    /// <summary>Remove least-recently-accessed entries if the cache exceeds the configured limit.</summary>
    private async Task TrimCacheAsync(CancellationToken ct)
    {
        var limit = await settingsService.GetIntSettingAsync(SettingsMap.EmbeddingQueryCacheLimit);
        if (limit <= 0) limit = 2000;

        var count = await db.SearchEmbeddings.CountAsync(ct);
        if (count <= limit) return;

        var toDelete = await db.SearchEmbeddings
            .OrderBy(e => e.LastAccessedAt)
            .Take(count - limit)
            .ToListAsync(ct);

        if (toDelete.Count > 0)
        {
            db.SearchEmbeddings.RemoveRange(toDelete);
            await db.SaveChangesAsync(ct);
        }
    }

    private class OllamaEmbedResponse
    {
        [JsonProperty("embeddings")]
        public float[][]? Embeddings { get; set; }
    }
}
