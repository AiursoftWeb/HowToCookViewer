using Aiursoft.HowToCookViewer.Services;
using Microsoft.Extensions.Caching.Memory;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class SearchRateLimiterTests
{
    private IMemoryCache _cache = null!;
    private SearchRateLimiter _rateLimiter = null!;

    [TestInitialize]
    public void Initialize()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _rateLimiter = new SearchRateLimiter(_cache);
    }

    [TestMethod]
    public void AllowsUpToMaxRequests_PerIpPerWindow()
    {
        // 8 requests (the max per minute) should all succeed.
        for (var i = 0; i < 8; i++)
        {
            Assert.IsTrue(_rateLimiter.TryConsume("192.168.1.1"),
                $"Request {i + 1} should be allowed within the rate limit.");
        }
    }

    [TestMethod]
    public void BlocksAfterExceedingLimit()
    {
        // Consume all 8 allowed requests.
        for (var i = 0; i < 8; i++)
        {
            Assert.IsTrue(_rateLimiter.TryConsume("192.168.1.1"));
        }

        // 9th request should be blocked.
        Assert.IsFalse(_rateLimiter.TryConsume("192.168.1.1"),
            "9th request within the same window should be rate limited.");
    }

    [TestMethod]
    public void DifferentIps_HaveIndependentLimits()
    {
        // Exhaust all requests for IP 1.
        for (var i = 0; i < 8; i++)
        {
            _rateLimiter.TryConsume("192.168.1.1");
        }

        // IP 1 is now rate limited.
        Assert.IsFalse(_rateLimiter.TryConsume("192.168.1.1"));

        // IP 2 should still be allowed (separate counter).
        for (var i = 0; i < 8; i++)
        {
            Assert.IsTrue(_rateLimiter.TryConsume("192.168.1.2"),
                $"IP 2 request {i + 1} should be allowed (independent limit).");
        }

        Assert.IsFalse(_rateLimiter.TryConsume("192.168.1.2"),
            "IP 2 should be rate limited after exceeding its own limit.");
    }

    [TestMethod]
    public void NullIp_UsesStringLiteral()
    {
        // "unknown" string should work as any other IP.
        for (var i = 0; i < 8; i++)
        {
            Assert.IsTrue(_rateLimiter.TryConsume("unknown"));
        }

        Assert.IsFalse(_rateLimiter.TryConsume("unknown"));
    }
}
