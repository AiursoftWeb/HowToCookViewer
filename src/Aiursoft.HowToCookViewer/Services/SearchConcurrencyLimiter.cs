namespace Aiursoft.HowToCookViewer.Services;

/// <summary>Shared process-local concurrency budget. Frequency limits belong to LimitPerMin.</summary>
public class SearchConcurrencyLimiter
{
    private const int MaxConcurrentRequests = 4;
    private readonly object _gate = new();
    private readonly HashSet<string> _activeIps = [];

    public IDisposable? TryAcquire(string ip)
    {
        lock (_gate)
        {
            if (_activeIps.Count >= MaxConcurrentRequests || !_activeIps.Add(ip)) return null;
            return new Lease(this, ip);
        }
    }

    private sealed class Lease(SearchConcurrencyLimiter owner, string ip) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            lock (owner._gate) owner._activeIps.Remove(ip);
        }
    }
}
