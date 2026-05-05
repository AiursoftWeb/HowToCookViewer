using System.Net;
using Aiursoft.DbTools;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.AspNetCore.Identity;
using Aiursoft.CSTools.Tools;
using static Aiursoft.WebTools.Extends;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class SpecialAccountTests
{
    private HttpClient _http = null!;
    private IHost _server = null!;
    private int _port;

    [TestInitialize]
    public async Task Setup()
    {
        _port = Network.GetAvailablePort();
        var cookieContainer = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookieContainer,
            AllowAutoRedirect = false
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://localhost:{_port}")
        };

        // Set environment variable for default role
        Environment.SetEnvironmentVariable("AppSettings__DefaultRole", "Administrators");
        
        _server = await AppAsync<Startup>([], port: _port);
        await _server.UpdateDbAsync<TemplateDbContext>();
        await _server.SeedAsync();
        await _server.StartAsync();
    }

    [TestCleanup]
    public async Task Teardown()
    {
        await _server.StopAsync();
        _server.Dispose();
        Environment.SetEnvironmentVariable("AppSettings__DefaultRole", null);
    }

    [TestMethod]
    public async Task TestRegisterWithDefaultRole()
    {
        var email = $"test-{Guid.NewGuid()}@aiursoft.com";
        var password = "Test-Password-123";

        // Register
        var registerResponse = await PostForm("/Account/Register", new Dictionary<string, string>
        {
            { "Email", email },
            { "Password", password },
            { "ConfirmPassword", password }
        });
        Assert.AreEqual(HttpStatusCode.Found, registerResponse.StatusCode);

        // Verify role
        using (var scope = _server.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var user = await userManager.FindByEmailAsync(email);
            Assert.IsNotNull(user);
            var roles = await userManager.GetRolesAsync(user);
            Assert.IsTrue(roles.Contains("Administrators"));
        }
    }

    private async Task<string> GetAntiCsrfToken(string url)
    {
        var response = await _http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = System.Text.RegularExpressions.Regex.Match(html,
            @"<input name=""__RequestVerificationToken"" type=""hidden"" value=""([^""]+)"" />");
        if (!match.Success) throw new InvalidOperationException("Could not find anti-CSRF token");
        return match.Groups[1].Value;
    }

    private async Task<HttpResponseMessage> PostForm(string url, Dictionary<string, string> data)
    {
        var token = await GetAntiCsrfToken(url);
        data["__RequestVerificationToken"] = token;
        return await _http.PostAsync(url, new FormUrlEncodedContent(data));
    }
}
