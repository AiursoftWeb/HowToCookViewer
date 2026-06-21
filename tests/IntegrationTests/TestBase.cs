using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using Aiursoft.CSTools.Tools;
using Aiursoft.DbTools;
using Aiursoft.HowToCookViewer.Entities;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

public abstract class TestBase
{
    private static readonly ConcurrentDictionary<string, ClassFixture> Fixtures = new();

    // Shared server — started lazily by the first ClassInit that fires,
    // then reused by every integration test class.
    private static readonly Lazy<Task<SharedServer>> LazyServer = new(async () =>
    {
        for (var attempt = 0; ; attempt++)
        {
            var port = Network.GetAvailablePort();
            var server = await AppAsync<Startup>([], port: port);
            await server.UpdateDbAsync<TemplateDbContext>();
            await server.SeedAsync();
            try
            {
                await server.StartAsync();
                return new SharedServer(port, server);
            }
            catch (IOException) when (attempt < 3)
            {
                await server.StopAsync();
                await server.DisposeAsync();
                await Task.Delay(100);
            }
        }
    });

    private static SharedServer? _cachedServer;

    private ClassFixture GetFixture() => Fixtures[GetType().FullName!];

    protected int Port => _cachedServer!.Port;
    protected HttpClient Http => GetFixture().Http;
    protected IHost Server => _cachedServer!.Host;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassInit(TestContext context)
    {
        // Lazy ensures the server is started exactly once — the first call
        // boots it up; all subsequent calls just await the cached task.
        _cachedServer = await LazyServer.Value;

        // Each test class gets an isolated HttpClient (cookie container per class
        // = isolated authentication sessions), but shares the single server instance.
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        var http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://localhost:{_cachedServer.Port}")
        };

        Fixtures[context.FullyQualifiedTestClassName] = new ClassFixture(http);
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static void ClassCleanup(TestContext context)
    {
        if (Fixtures.TryRemove(context.FullyQualifiedTestClassName, out var fixture))
        {
            fixture.Http.Dispose();
        }
    }

    [AssemblyCleanup]
    public static void AssemblyCleanup()
    {
        if (_cachedServer != null)
        {
            _cachedServer.Host.StopAsync().GetAwaiter().GetResult();
            _cachedServer.Host.Dispose();
        }
    }

    protected async Task<string> GetAntiCsrfToken(string url)
    {
        var response = await Http.GetAsync(url);
        if (!response.IsSuccessStatusCode)
        {
            response = await Http.GetAsync("/");
        }
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(html,
            @"<input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)"" />");
        if (!match.Success)
        {
            throw new InvalidOperationException($"Could not find anti-CSRF token on page: {url}");
        }

        return match.Groups[1].Value;
    }

    protected async Task<HttpResponseMessage> PostForm(string url, Dictionary<string, string> data, string? tokenUrl = null, bool includeToken = true)
    {
        if (includeToken && !data.ContainsKey("__RequestVerificationToken"))
        {
            var token = await GetAntiCsrfToken(tokenUrl ?? url);
            data["__RequestVerificationToken"] = token;
        }
        return await Http.PostAsync(url, new FormUrlEncodedContent(data));
    }

    protected void AssertRedirect(HttpResponseMessage response, string expectedLocation, bool exact = true)
    {
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        var actualLocation = response.Headers.Location?.OriginalString ?? string.Empty;
        var baseUri = Http.BaseAddress?.ToString() ?? "____";

        if (actualLocation.StartsWith(baseUri))
        {
            actualLocation = actualLocation.Substring(baseUri.Length - 1);
        }

        if (exact)
        {
            Assert.AreEqual(expectedLocation, actualLocation, $"Expected redirect to {expectedLocation}, but was {actualLocation}");
        }
        else
        {
            Assert.StartsWith(expectedLocation, actualLocation);
        }
    }

    protected async Task LoginAsAdmin()
    {
        var loginResponse = await PostForm("/Account/Login", new Dictionary<string, string>
        {
            { "EmailOrUserName", "admin@default.com" },
            { "Password", "Admin@123456!" }
        });
        Assert.AreEqual(HttpStatusCode.Found, loginResponse.StatusCode);
    }

    protected async Task<(string email, string password)> RegisterAndLoginAsync()
    {
        var email = $"test-{Guid.NewGuid()}@aiursoft.com";
        var password = "Test-Password-123";

        var registerResponse = await PostForm("/Account/Register", new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password }
        });
        Assert.AreEqual(HttpStatusCode.Found, registerResponse.StatusCode);

        return (email, password);
    }

    protected T GetService<T>() where T : notnull
    {
        if (Server == null) throw new InvalidOperationException("Server is not started.");
        return Server.Services.GetRequiredService<T>();
    }

    private sealed record SharedServer(int Port, IHost Host);
    private sealed record ClassFixture(HttpClient Http);
}
