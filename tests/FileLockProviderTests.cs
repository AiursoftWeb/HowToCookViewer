using Aiursoft.HowToCookViewer.Services.FileStorage;
using Microsoft.Extensions.Caching.Memory;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class FileLockProviderTests
{
    [TestMethod]
    public async Task TestGetLock()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var provider = new FileLockProvider(cache);
        
        var lock1 = provider.GetLock("path1");
        var lock2 = provider.GetLock("path1");
        var lock3 = provider.GetLock("path2");
        
        Assert.AreSame(lock1, lock2);
        Assert.AreNotSame(lock1, lock3);
        
        await lock1.WaitAsync();
        // Should not be able to acquire lock2 (same instance)
        var acquired = await lock2.WaitAsync(100);
        Assert.IsFalse(acquired);
        
        lock1.Release();
        acquired = await lock2.WaitAsync(100);
        Assert.IsTrue(acquired);
        lock2.Release();
    }
}
