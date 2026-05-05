using System.Net;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class AccountControllerTests : TestBase
{
    [TestMethod]
    public async Task GetLogin()
    {
        var url = "/Account/Login";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task GetRegister()
    {
        var url = "/Account/Register";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task RegisterFailure()
    {
        // Invalid model (empty email)
        var response = await PostForm("/Account/Register", new Dictionary<string, string>
        {
            { "Email", "" },
            { "Password", "Password123!" },
            { "ConfirmPassword", "Password123!" }
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task RegisterDuplicateUserName()
    {
        // First registration
        var email1 = $"user-{Guid.NewGuid()}@a.com";
        await PostForm("/Account/Register", new Dictionary<string, string>
        {
            { "Email", email1 },
            { "Password", "Password123!" },
            { "ConfirmPassword", "Password123!" }
        });

        // Second registration with same prefix but different domain
        var email2 = email1.Split('@')[0] + "@b.com";
        var response = await PostForm("/Account/Register", new Dictionary<string, string>
        {
            { "Email", email2 },
            { "Password", "Password123!" },
            { "ConfirmPassword", "Password123!" }
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("The username already exists. Please try another username.", html);
    }
}
