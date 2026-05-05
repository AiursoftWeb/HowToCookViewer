using System.Net;
using Aiursoft.HowToCookViewer.Authorization;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class PermissionsControllerTests : TestBase
{
    [TestMethod]
    public async Task GetIndex()
    {
        await LoginAsAdmin();
        var url = "/Permissions/Index";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Permissions", html);
    }

    [TestMethod]
    public async Task GetDetails()
    {
        await LoginAsAdmin();
        var url = $"/Permissions/Details?key={AppPermissionNames.CanReadPermissions}";
        var response = await Http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Permission Details", html);
    }

    [TestMethod]
    public async Task GetDetailsInvalidKey()
    {
        await LoginAsAdmin();
        var url = "/Permissions/Details?key=invalid";
        var response = await Http.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task GetDetailsNullKey()
    {
        await LoginAsAdmin();
        var url = "/Permissions/Details";
        var response = await Http.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
