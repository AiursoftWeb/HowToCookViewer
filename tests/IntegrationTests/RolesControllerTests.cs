using System.Net;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class RolesControllerTests : TestBase
{
    [TestMethod]
    public async Task TestRolesWorkflow()
    {
        await LoginAsAdmin();

        // 1. Index
        var indexResponse = await Http.GetAsync("/Roles/Index");
        indexResponse.EnsureSuccessStatusCode();
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.Contains("Administrators", indexHtml);

        // 2. Create
        var roleName = "TestRole-" + Guid.NewGuid();
        var createResponse = await PostForm("/Roles/Create", new Dictionary<string, string>
        {
            { "RoleName", roleName }
        });
        AssertRedirect(createResponse, "/Roles/Details/", exact: false);
        var roleId = createResponse.Headers.Location?.OriginalString.Split("/").Last();
        Assert.IsNotNull(roleId);

        // 3. Details
        var detailsResponse = await Http.GetAsync($"/Roles/Details/{roleId}");
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(roleName, detailsHtml);

        // 4. Edit (GET)
        var editPageResponse = await Http.GetAsync($"/Roles/Edit/{roleId}");
        editPageResponse.EnsureSuccessStatusCode();

        // 5. Edit (POST)
        var newRoleName = roleName + "-Edited";
        var editContent = new Dictionary<string, string>
        {
            { "Id", roleId },
            { "RoleName", newRoleName },
            { "Claims[0].Key", "CanReadPermissions" },
            { "Claims[0].Name", "CanReadPermissions" },
            { "Claims[0].Description", "CanReadPermissions" },
            { "Claims[0].IsSelected", "true" }
        };
        var editResponse = await PostForm($"/Roles/Edit/{roleId}", editContent);
        AssertRedirect(editResponse, "/Roles/Details/", exact: false);

        // Verify edit
        var detailsResponse2 = await Http.GetAsync($"/Roles/Details/{roleId}");
        var detailsHtml2 = await detailsResponse2.Content.ReadAsStringAsync();
        Assert.Contains(newRoleName, detailsHtml2);

        // 6. Delete (GET)
        var deletePageResponse = await Http.GetAsync($"/Roles/Delete/{roleId}");
        deletePageResponse.EnsureSuccessStatusCode();

        // 7. Delete (POST)
        var deleteResponse = await PostForm($"/Roles/Delete/{roleId}", new Dictionary<string, string>
        {
            { "id", roleId }
        });
        AssertRedirect(deleteResponse, "/Roles");

        // Verify delete
        var indexResponse2 = await Http.GetAsync("/Roles/Index");
        var indexHtml2 = await indexResponse2.Content.ReadAsStringAsync();
        Assert.DoesNotContain(newRoleName, indexHtml2);
    }

    [TestMethod]
    public async Task TestCreateRoleFailure()
    {
        await LoginAsAdmin();

        // Test with invalid model (empty name)
        var response = await PostForm("/Roles/Create", new Dictionary<string, string>
        {
            { "RoleName", "" }
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task TestEditRoleFailure()
    {
        await LoginAsAdmin();
        var roleName = "TestRole-" + Guid.NewGuid();
        await PostForm("/Roles/Create", new Dictionary<string, string> { { "RoleName", roleName } });
        
        var indexResponse = await Http.GetAsync("/Roles/Index");
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        // Extract role ID from HTML
        var match = System.Text.RegularExpressions.Regex.Match(indexHtml, @"/Roles/Details/([^""]+)");
        var roleId = match.Groups[1].Value;

        // Test with invalid model (empty name)
        var response = await PostForm($"/Roles/Edit/{roleId}", new Dictionary<string, string>
        {
            { "Id", roleId },
            { "RoleName", "" }
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task TestDetailsNotFound()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Roles/Details/invalid-id");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestDetailsNullId()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Roles/Details/");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestDeleteConfirmedNotFound()
    {
        await LoginAsAdmin();
        var response = await PostForm("/Roles/Delete/invalid-id", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestEditNotFound()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Roles/Edit/invalid-id");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestDeleteNotFound()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Roles/Delete/invalid-id");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
