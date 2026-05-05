using Microsoft.Extensions.Caching.Memory;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.HowToCookViewer.Services.FileStorage;

/// <summary>
/// Provides a thread-safe mechanism to lock on certain file paths
/// so that concurrent read/write operations do not clash.
/// Uses an in-memory cache with sliding expiration to prevent memory leaks from excessive unique paths.
/// </summary>
public class FileLockProvider(IMemoryCache lockCache) : ITransientDependency
{
    /// <summary>
    /// Retrieves or creates a lock semaphore for the specified path from the cache.
    /// The semaphore will be automatically removed from memory if unused for a certain period.
    /// </summary>
    public SemaphoreSlim GetLock(string path)
    {
        // GetOrCreate is thread-safe. It ensures only one semaphore is created per key.
        return lockCache.GetOrCreate($"file-lock-cache-with-path-{path}", entry =>
        {
            // Set a sliding expiration. If the lock is not accessed for 5 minutes,
            // it will be removed from the cache. This prevents the memory leak.
            entry.SetSlidingExpiration(TimeSpan.FromMinutes(5));
            return new SemaphoreSlim(1, 1);
        })!;
    }
}
