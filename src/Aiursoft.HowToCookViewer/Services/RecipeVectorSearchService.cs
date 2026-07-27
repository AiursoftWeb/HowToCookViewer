using System.Security.Cryptography;
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
    IHttpClientFactory httpClientFactory,
    ILogger<RecipeVectorSearchService> logger)
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
        if (!await ShouldAttemptVectorSearchAsync())
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
            var expectedDimension = snapshot.Values.First().First().Length;
            queryVector = await EmbedQueryAsync(query, expectedDimension, ct);
        }
        catch (Exception)
        {
            return (false, [], 0);
        }

        if (queryVector == null)
        {
            return (false, [], 0);
        }

        // Compute cosine similarity for all cached recipes.
        // Each recipe has multiple embeddings (Chinese + localizations).
        // Take the best-matching language so that, e.g., an English query
        // naturally matches against the English localization embedding.
        var scored = new List<(int RecipeId, float Score)>();
        var skippedDimensionMismatch = 0;
        foreach (var kv in snapshot)
        {
            float maxScore = float.MinValue;
            bool anyValid = false;
            foreach (var e in kv.Value)
            {
                if (e.Length != queryVector.Length)
                {
                    skippedDimensionMismatch++;
                    continue;
                }
                anyValid = true;
                var score = EmbeddingHelper.CosineSimilarity(queryVector, e);
                if (score > maxScore)
                {
                    maxScore = score;
                }
            }
            if (anyValid && maxScore > 0)
            {
                scored.Add((kv.Key, maxScore));
            }
        }

        if (scored.Count == 0 && skippedDimensionMismatch > 0)
        {
            logger.LogWarning(
                "Vector search skipped {Count} recipe embeddings because their dimensions did not match the query vector.",
                skippedDimensionMismatch);
            return (false, [], 0);
        }

        scored = scored
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
        if (!snapshot.TryGetValue(recipeId, out var targetVectors) || targetVectors.Count == 0)
        {
            return [];
        }

        // Compare all target embeddings (Chinese + localizations) against all candidate embeddings.
        var scored = new List<(int RecipeId, float Score)>();
        foreach (var kv in snapshot)
        {
            if (kv.Key == recipeId)
                continue;

            float bestScore = float.MinValue;
            foreach (var targetVec in targetVectors)
            {
                foreach (var candidateVec in kv.Value)
                {
                    if (candidateVec.Length != targetVec.Length)
                        continue;
                    var score = EmbeddingHelper.CosineSimilarity(targetVec, candidateVec);
                    if (score > bestScore)
                        bestScore = score;
                }
            }
            if (bestScore > 0)
            {
                scored.Add((kv.Key, bestScore));
            }
        }

        var topIds = scored
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

    private static string ComputeQueryCacheKey(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var sb = new StringBuilder(40);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2"));
            if (sb.Length >= 40) break;
        }

        return sb.ToString();
    }

    private async Task<bool> ShouldAttemptVectorSearchAsync()
    {
        var enabled = await settingsService.GetBoolSettingAsync(SettingsMap.EnableEmbeddingBasedSearch);
        if (!enabled) return false;

        var endpoint = await settingsService.GetEmbeddingEndpointAsync();
        if (string.IsNullOrWhiteSpace(endpoint)) return false;

        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        return !string.IsNullOrWhiteSpace(model);
    }

    private async Task<float[]?> EmbedQueryAsync(string text, int expectedDimension, CancellationToken ct)
    {
        // Hash the full query text for the cache key. The QueryText column is capped at 40 chars with a
        // unique index, so we keep the first 40 hex chars of the SHA-256 digest. Hashing the full text
        // (instead of truncating the raw text) avoids collisions between queries that share a long common
        // prefix but differ later — those used to return each other's cached embedding.
        var cacheKey = ComputeQueryCacheKey(text);

        // Check database cache first.
        var cached = await db.SearchEmbeddings
            .FirstOrDefaultAsync(e => e.QueryText == cacheKey, ct);

        if (cached != null)
        {
            var vector = EmbeddingHelper.Deserialize(cached.Embedding);
            if (vector != null && vector.Length == expectedDimension)
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

            db.SearchEmbeddings.Remove(cached);
            await db.SaveChangesAsync(ct);
        }

        // Compute embedding via Ollama.
        var instance = await settingsService.GetEmbeddingEndpointAsync();
        var model = await settingsService.GetSettingValueAsync(SettingsMap.EmbeddingModel);
        var token = await settingsService.GetEmbeddingTokenAsync();

        // Truncate query text to fit bge-m3's 8192-token context window.
        // Queries are typically short, but a user might paste a very long document.
        const int maxQueryChars = 8000;
        var input = text.Length > maxQueryChars ? text[..maxQueryChars] : text;

        var http = httpClientFactory.CreateClient();
        var baseUri = new Uri(instance);
        var embedEndpoint = $"{baseUri.Scheme}://{baseUri.Authority}/api/embed?keep_alive=-1";
        var requestBody = new { model, input };
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
                QueryText = cacheKey,
                Embedding = serialized,
                CreatedAt = now,
                LastAccessedAt = now
            });
            await db.SaveChangesAsync(ct);

            // Trim to MaxCachedQueries entries (circular buffer).
            await TrimCacheAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // Race: another request already cached this query. Ignore.
            logger.LogWarning(ex, "Failed to cache query embedding for '{Query}'. Likely a concurrent duplicate.", text);
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
