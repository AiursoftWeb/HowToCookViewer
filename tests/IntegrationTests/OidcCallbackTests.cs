using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.DbTools;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class OidcCallbackTests
{
    private readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private IHost? Server;
    private readonly string _storagePath = Path.Combine(Path.GetTempPath(), $"oidc-callback-tests-{Guid.NewGuid():N}");
    private readonly ErrorLogCapture _logs = new();
    private OpenIdConnectOptions _options = null!;

    [TestInitialize]
    public async Task CreateServer()
    {
        using var reservation = new TestPortReservation();
        Http.BaseAddress = new Uri($"http://localhost:{reservation.Port}");
        Server = await AppAsync<Startup>([
            $"Storage:Path={_storagePath}",
            "AppSettings:AuthProvider=OIDC",
            "AppSettings:OIDC:Authority=https://idp.example.com",
            "AppSettings:OIDC:ClientId=callback-tests",
            "AppSettings:OIDC:ClientSecret=test-secret"
        ], port: reservation.Port);
        try
        {
            reservation.ConfigureServer(Server);
            Server.Services.GetRequiredService<ILoggerFactory>().AddProvider(_logs);
            _options = Server.Services.GetRequiredService<IOptionsMonitor<OpenIdConnectOptions>>()
                .Get(OpenIdConnectDefaults.AuthenticationScheme);
            _options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                new OpenIdConnectConfiguration
                {
                    Issuer = "https://idp.example.com",
                    AuthorizationEndpoint = "https://idp.example.com/authorize",
                    TokenEndpoint = "https://idp.example.com/token"
                });
            await Server.UpdateDbAsync<TemplateDbContext>();
            await Server.SeedAsync();
            await Server.StartAsync();
        }
        catch
        {
            Server.Dispose();
            Server = null;
            throw;
        }
    }

    [TestCleanup]
    public async Task CleanServer()
    {
        if (Server != null)
        {
            await Server.StopAsync();
            Server.Dispose();
        }
        Http.Dispose();
        if (Directory.Exists(_storagePath)) Directory.Delete(_storagePath, recursive: true);
    }

    [TestMethod]
    [DataRow("/signin-oidc")]
    [DataRow("/signin-oidc?state=")]
    [DataRow("/signin-oidc?state=invalid&code=untrusted")]
    [DataRow("/signin-oidc?error=access_denied")]
    public async Task InvalidCallbackShowsBadRequestWithoutErrors(string path)
    {
        var response = await Http.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Bad Request", html);
        Assert.Contains("Back to Home", html);
        Assert.IsFalse(response.Headers.Contains("Set-Cookie"), "An invalid callback must not sign out an existing session.");
        Assert.AreEqual(0, _logs.Errors.Count);
    }

    [TestMethod]
    public async Task RefreshingTheCallbackDoesNotProduceAnIncident()
    {
        for (var i = 0; i < 4; i++)
        {
            var response = await Http.GetAsync("/signin-oidc");
            Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        }
        Assert.AreEqual(0, _logs.Errors.Count);
    }

    [TestMethod]
    public async Task FormPostWithoutStateIsRejected()
    {
        var response = await Http.PostAsync("/signin-oidc", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["code"] = "untrusted" }));
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, _logs.Errors.Count);
    }

    [TestMethod]
    public async Task ProtectedStateWithoutCorrelationCookieCannotSignIn()
    {
        var properties = new AuthenticationProperties();
        properties.Items[".xsrf"] = "expired-correlation";
        var state = _options.StateDataFormat.Protect(properties);
        var response = await Http.GetAsync($"/signin-oidc?state={Uri.EscapeDataString(state)}&code=untrusted");
        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.AreEqual(0, _logs.Errors.Count);
    }

    [TestMethod]
    public async Task ValidLoginChallengeStillProducesProtectedStateAndCorrelationCookie()
    {
        var response = await Http.GetAsync("/Account/Login");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.IsNotNull(response.Headers.Location);
        Assert.StartsWith("https://idp.example.com/authorize", response.Headers.Location.AbsoluteUri);
        Assert.Contains("state=", response.Headers.Location.Query);
        Assert.IsTrue(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.IsNotNull(cookies);
        Assert.IsTrue(cookies.Any(cookie => cookie.StartsWith(".AspNetCore.Correlation.", StringComparison.Ordinal)));
        Assert.AreEqual(0, _logs.Errors.Count);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ValidStateAndCorrelationReachCodeRedemption(bool formPost)
    {
        var challenge = await Http.GetAsync("/Account/Login");
        Assert.AreEqual(HttpStatusCode.Found, challenge.StatusCode);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(challenge.Headers.Location!.Query);
        var state = query["state"].ToString();
        var cookies = challenge.Headers.GetValues("Set-Cookie")
            .Select(cookie => cookie.Split(';')[0]);
        var reachedCodeRedemption = false;
        _options.Events.OnAuthorizationCodeReceived = context =>
        {
            reachedCodeRedemption = true;
            context.HandleResponse();
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        };
        using var request = new HttpRequestMessage(formPost ? HttpMethod.Post : HttpMethod.Get,
            formPost ? "/signin-oidc" : $"/signin-oidc?state={Uri.EscapeDataString(state)}&code=test-code");
        request.Headers.Add("Cookie", string.Join("; ", cookies));
        if (formPost)
        {
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["state"] = state,
                ["code"] = "test-code"
            });
        }
        var response = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        Assert.IsTrue(reachedCodeRedemption);
        Assert.AreEqual(0, _logs.Errors.Count);
    }

    [TestMethod]
    [DataRow("invalid_grant", true)]
    [DataRow("invalid_client", false)]
    [DataRow("server_error", false)]
    public async Task ProtocolFailuresOnlyRecoverForExpectedLoginErrors(string error, bool recoverable)
    {
        var exception = new OpenIdConnectProtocolException("Provider rejected the request.");
        exception.Data["error"] = error;
        await AssertFailure(exception, recoverable);
    }

    [TestMethod]
    public async Task InternalAuthenticationFailuresRemainServerErrors()
    {
        await AssertFailure(new AuthenticationFailureException("Authentication infrastructure failed.",
            new InvalidOperationException("Database unavailable.")), false);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ProviderProfileAndAccountSynchronizationFailuresRemainInternal(bool invalidRole)
    {
        using var scope = Server!.Services.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<IOptions<AppSettings>>().Value.OIDC;
        var claims = invalidRole
            ? new[]
            {
                new Claim(settings.UsernamePropertyName, "callback-user"),
                new Claim(settings.UserDisplayNamePropertyName, "Callback User"),
                new Claim(settings.EmailPropertyName, "callback-user@example.com"),
                new Claim(settings.UserIdentityPropertyName, "callback-subject"),
                new Claim(settings.RolePropertyName, new string('x', 257))
            }
            : Array.Empty<Claim>();
        var context = new TokenValidatedContext(
            new DefaultHttpContext { RequestServices = scope.ServiceProvider },
            new AuthenticationScheme(OpenIdConnectDefaults.AuthenticationScheme, null, typeof(OpenIdConnectHandler)),
            _options,
            new ClaimsPrincipal(new ClaimsIdentity(claims, "test")),
            new AuthenticationProperties());
        await _options.Events.TokenValidated(context);
        Assert.IsNotNull(context.Result.Failure);
        Assert.IsInstanceOfType<InvalidOperationException>(context.Result.Failure);
        await AssertFailure(context.Result.Failure, false);
    }

    private async Task AssertFailure(Exception exception, bool recoverable)
    {
        // Fail from the real OIDC handler after message parsing, without contacting a provider.
        _options.Events.OnMessageReceived = context =>
        {
            context.Fail(exception);
            return Task.CompletedTask;
        };
        var response = await Http.GetAsync("/signin-oidc");
        if (recoverable)
        {
            Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
            Assert.AreEqual("/Account/Login", response.Headers.Location?.OriginalString);
            Assert.AreEqual(0, _logs.Errors.Count);
        }
        else
        {
            Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.AreEqual(1, _logs.Errors.Count);
            Assert.IsFalse(_logs.Errors.Any(category => category == typeof(Startup).FullName),
                "The application must not duplicate the exception middleware's Error log.");
        }
    }

    private sealed class ErrorLogCapture : ILoggerProvider
    {
        public ConcurrentQueue<string> Errors { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName, Errors);
        public void Dispose() { }

        private sealed class CaptureLogger(string category, ConcurrentQueue<string> errors) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel)) errors.Enqueue(category);
            }
        }
    }
}
