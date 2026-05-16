using Microsoft.Extensions.Caching.Memory;

namespace Aiursoft.HowToCookViewer.Services;

/// <summary>
/// Per-IP rate limiter for search queries. Uses sliding-window counters in memory.
/// </summary>
public class SearchRateLimiter(IMemoryCache cache)
{
    private const int MaxRequestsPerMinute = 8;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>Returns true if the IP is allowed to search; false if rate limited.</summary>
    public bool TryConsume(string ip)
    {
        var key = $"search-rate:{ip}";
        var now = DateTimeOffset.UtcNow;

        if (cache.TryGetValue(key, out SlidingCounter? counter) && counter != null)
        {
            counter.Prune(now);
            if (counter.Count >= MaxRequestsPerMinute)
            {
                return false;
            }

            counter.Increment(now);
            cache.Set(key, counter, Window);
            return true;
        }

        counter = new SlidingCounter();
        counter.Increment(now);
        cache.Set(key, counter, Window);
        return true;
    }

    private class SlidingCounter
    {
        private readonly List<DateTimeOffset> _hits = [];

        public int Count => _hits.Count;

        public void Increment(DateTimeOffset now)
        {
            _hits.Add(now);
        }

        public void Prune(DateTimeOffset now)
        {
            var cutoff = now - Window;
            _hits.RemoveAll(h => h < cutoff);
        }
    }
}
