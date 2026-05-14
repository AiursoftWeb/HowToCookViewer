using System.Diagnostics.CodeAnalysis;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Services;

/// <summary>
/// In-memory cache of recipe embedding vectors for fast cosine-similarity search.
/// Loaded at startup and refreshed periodically via RefreshEmbeddingCacheJob.
/// </summary>
[ExcludeFromCodeCoverage]
public class RecipeEmbeddingCache
{
    private Dictionary<int, float[]> _cache = [];
    private readonly object _lock = new();

    public int Count
    {
        get { lock (_lock) return _cache.Count; }
    }

    /// <summary>Returns a snapshot of the current cache for search.</summary>
    public Dictionary<int, float[]> Snapshot()
    {
        lock (_lock) return new Dictionary<int, float[]>(_cache);
    }

    public async Task LoadAsync(TemplateDbContext db)
    {
        var embeddings = await db.Recipes
            .AsNoTracking()
            .Where(r => r.Embedding != null)
            .Select(r => new { r.Id, r.Embedding })
            .ToListAsync();

        var newCache = new Dictionary<int, float[]>();
        foreach (var item in embeddings)
        {
            var vector = Deserialize(item.Embedding!);
            if (vector != null)
            {
                newCache[item.Id] = vector;
            }
        }

        lock (_lock)
        {
            _cache = newCache;
        }
    }

    private static float[]? Deserialize(byte[] bytes)
    {
        if (bytes.Length % 4 != 0) return null;
        var floats = new float[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }
}
