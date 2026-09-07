using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Aiursoft.HowToCookViewer.Services;

/// <summary>Reject before model binding/queries and retain the permit through result rendering.</summary>
public class SearchRequestFilter(SearchConcurrencyLimiter limiter) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var address = context.HttpContext.Connection.RemoteIpAddress;
        var ip = address?.MapToIPv6().ToString() ?? "unknown";
        using var lease = limiter.TryAcquire(ip);
        if (lease == null)
        {
            context.HttpContext.Response.Headers.RetryAfter = "1";
            context.Result = new ContentResult
            {
                StatusCode = StatusCodes.Status429TooManyRequests,
                ContentType = "text/plain; charset=utf-8",
                Content = "Search is busy. Please wait and try again."
            };
            return;
        }

        context.HttpContext.RequestAborted.ThrowIfCancellationRequested();
        await next();
    }
}
