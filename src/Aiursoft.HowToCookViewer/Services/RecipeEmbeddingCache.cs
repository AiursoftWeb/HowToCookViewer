using System.Diagnostics.CodeAnalysis;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Util;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Services;

/// <summary>
/// In-memory cache of recipe embedding vectors for fast cosine-similarity search.
/// Each recipe may have multiple embeddings: the original Chinese embedding on Recipe,
/// plus one per localized language on LocalizedRecipe. At search time the best-matching
/// language is used per recipe — no language detection needed.
/// Loaded at startup and refreshed periodically via RefreshEmbeddingCacheJob.
/// </summary>
[ExcludeFromCodeCoverage]
public class RecipeEmbeddingCache(ILogger<RecipeEmbeddingCache> logger)
{
    private Dictionary<int, List<float[]>> _cache = [];
    private readonly Lock _lock = new();
    private const int MaxEntries = 10_000;

    public int Count
    {
        get { lock (_lock) return _cache.Count; }
    }

    /// <summary>Returns a snapshot of the current cache for search.</summary>
    public Dictionary<int, List<float[]>> Snapshot()
    {
        lock (_lock)
        {
            var copy = new Dictionary<int, List<float[]>>(_cache.Count);
            foreach (var (key, list) in _cache)
            {
                copy[key] = new List<float[]>(list);
            }
            return copy;
        }
    }

    public async Task LoadAsync(TemplateDbContext db)
    {
        var newCache = new Dictionary<int, List<float[]>>();

        // Load Chinese embeddings from Recipe table.
        var chineseEmbeddings = await db.Recipes
            .AsNoTracking()
            .Where(r => r.Embedding != null)
            .Select(r => new { r.Id, r.Embedding })
            .ToListAsync();

        foreach (var item in chineseEmbeddings)
        {
            var vector = EmbeddingHelper.Deserialize(item.Embedding!);
            if (vector != null)
            {
                if (!newCache.TryGetValue(item.Id, out var list))
                {
                    list = [];
                    newCache[item.Id] = list;
                }
                list.Add(vector);
            }
            else
            {
                logger.LogWarning(
                    "Failed to deserialize embedding for recipe {RecipeId}: byte length {Length} is not a multiple of 4.",
                    item.Id, item.Embedding!.Length);
            }
        }

        // Load localized embeddings from LocalizedRecipe table.
        var localizedEmbeddings = await db.LocalizedRecipes
            .AsNoTracking()
            .Where(lr => lr.Embedding != null)
            .Select(lr => new { lr.RecipeId, lr.Embedding })
            .ToListAsync();

        foreach (var item in localizedEmbeddings)
        {
            var vector = EmbeddingHelper.Deserialize(item.Embedding!);
            if (vector != null)
            {
                if (!newCache.TryGetValue(item.RecipeId, out var list))
                {
                    list = [];
                    newCache[item.RecipeId] = list;
                }
                list.Add(vector);
            }
        }

        if (newCache.Count > MaxEntries)
        {
            logger.LogWarning(
                "RecipeEmbeddingCache: loaded {Count} entries exceeds MaxEntries ({MaxEntries}), capping.",
                newCache.Count, MaxEntries);
            newCache = newCache.Take(MaxEntries).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        lock (_lock)
        {
            _cache = newCache;
        }
    }
}
