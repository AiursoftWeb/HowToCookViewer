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

    private ClassFixture GetFixture() => Fixtures[GetType().FullName!];

    protected int Port => GetFixture().Port;
    protected HttpClient Http => GetFixture().Http;
    protected IHost Server => GetFixture().Server;

    [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
    public static async Task ClassInit(TestContext context)
    {
        // Retry on port conflict — rare with GetAvailablePort but can happen
        // under high concurrency with many test classes starting servers.
        for (var attempt = 0; ; attempt++)
        {
            var port = Network.GetAvailablePort();
            var cookieContainer = new CookieContainer();
            var handler = new HttpClientHandler
            {
                CookieContainer = cookieContainer,
                AllowAutoRedirect = false
            };
            var http = new HttpClient(handler)
            {
                BaseAddress = new Uri($"http://localhost:{port}")
            };

            var server = await AppAsync<Startup>([], port: port);
            await server.UpdateDbAsync<TemplateDbContext>();
            await server.SeedAsync();
            try
            {
                await server.StartAsync();
                Fixtures[context.FullyQualifiedTestClassName] = new ClassFixture(port, http, server);
                return;
            }
            catch (IOException) when (attempt < 3)
            {
                await server.StopAsync();
                await server.DisposeAsync();
                await Task.Delay(100);
            }
        }
    }

    [ClassCleanup(InheritanceBehavior.BeforeEachDerivedClass)]
    public static void ClassCleanup(TestContext context)
    {
        if (Fixtures.TryRemove(context.FullyQualifiedTestClassName, out var fixture))
        {
            fixture.Server.StopAsync().GetAwaiter().GetResult();
            fixture.Server.Dispose();
            fixture.Http.Dispose();
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

    private sealed record ClassFixture(int Port, HttpClient Http, IHost Server);
}
