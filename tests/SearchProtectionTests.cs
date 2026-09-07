using System.Collections.Concurrent;
using System.Net;
using Aiursoft.HowToCookViewer.Controllers;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
[DoNotParallelize]
public class SearchProtectionTests
{
    [TestMethod]
    public void ConcurrentRequests_CannotExceedGlobalOrPerIpBudget()
    {
        var limiter = new SearchConcurrencyLimiter();
        var leases = new ConcurrentBag<IDisposable>();
        Parallel.For(0, 100, i =>
        {
            var lease = limiter.TryAcquire(i.ToString());
            if (lease != null) leases.Add(lease);
        });
        Assert.HasCount(4, leases);
        foreach (var lease in leases) lease.Dispose();
        var first = limiter.TryAcquire("same-ip");
        Assert.IsNotNull(first);
        Assert.IsNull(limiter.TryAcquire("same-ip"));
        first.Dispose();
        using var second = limiter.TryAcquire("same-ip");
        Assert.IsNotNull(second);
        first.Dispose(); // An old permit cannot release the new one.
        Assert.IsNull(limiter.TryAcquire("same-ip"));
    }

    [TestMethod]
    public async Task ResourceFilter_RejectsBeforeWorkAndReleasesOnFailure()
    {
        var limiter = new SearchConcurrencyLimiter();
        var filter = new SearchRequestFilter(limiter);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Loopback;
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        ResourceExecutingContext Context() => new(action, [], new List<IValueProviderFactory>());
        using (limiter.TryAcquire(IPAddress.Loopback.MapToIPv6().ToString()))
        {
            var rejected = Context();
            await filter.OnResourceExecutionAsync(rejected, () => throw new AssertFailedException("Rejected request executed work."));
            Assert.AreEqual(429, ((ContentResult)rejected.Result!).StatusCode);
        }
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            filter.OnResourceExecutionAsync(Context(), () => throw new InvalidOperationException("Action failed")));
        using var lease = limiter.TryAcquire(IPAddress.Loopback.MapToIPv6().ToString());
        Assert.IsNotNull(lease);
    }

    [TestMethod]
    public void Endpoints_ReuseLimitPerMinAndRejectNinthRequest()
    {
        var previous = LimitPerMin.KeepFunctioningInUnitTest;
        LimitPerMin.KeepFunctioningInUnitTest = true;
        try
        {
            foreach (var (type, method) in new[] { (typeof(DashboardController), "Index"), (typeof(IngredientsController), "Lookup") })
            {
                using var services = new ServiceCollection().AddMemoryCache().AddLogging().BuildServiceProvider();
                var http = new DefaultHttpContext { RequestServices = services };
                http.Connection.RemoteIpAddress = IPAddress.Loopback;
                http.Request.Path = $"/{type.Name}/{method}";
                var limit = type.GetMethod(method)!.GetCustomAttributes(typeof(LimitPerMin), true).Cast<LimitPerMin>().Single();
                var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
                for (var i = 0; i < 9; i++)
                {
                    http.Response.Headers.Clear();
                    var context = new ActionExecutingContext(action, [], new Dictionary<string, object?>(), new object());
                    limit.OnActionExecuting(context);
                    if (i < 8) Assert.IsNull(context.Result);
                    else Assert.AreEqual(429, ((StatusCodeResult)context.Result!).StatusCode);
                }
            }
        }
        finally { LimitPerMin.KeepFunctioningInUnitTest = previous; }
    }
}
