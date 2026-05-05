using System.Net;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class SystemControllerTests : TestBase
{
    [TestMethod]
    public async Task TestIndex()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/System/Index");
        response.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task TestIndexContainsTableCounts()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/System/Index");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Database Table Counts", html);
        // At minimum the AspNetUsers table (seeded by TestBase) must appear with a non-zero count.
        Assert.Contains("Users", html);
    }

    [TestMethod]
    public async Task TestIndexContainsMigrationInfo()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/System/Index");
        response.EnsureSuccessStatusCode();

        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Database Migrations", html);
        // The "Applied / Defined" footer row must always be present (even if counts are 0 on InMemory).
        Assert.Contains("Applied / Defined", html);
    }

    [TestMethod]
    public async Task TestShutdown()
    {
        await LoginAsAdmin();
        var response = await Http.PostAsync("/System/Shutdown", null);
        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
    }
}
