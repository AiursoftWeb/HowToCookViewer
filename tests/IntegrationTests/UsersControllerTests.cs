using System.Net;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class UsersControllerTests : TestBase
{
    [TestMethod]
    public async Task TestUsersWorkflow()
    {
        await LoginAsAdmin();

        // 1. Index
        var indexResponse = await Http.GetAsync("/Users/Index");
        indexResponse.EnsureSuccessStatusCode();
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.Contains("admin", indexHtml);

        // 2. Create
        var userName = "testuser-" + Guid.NewGuid();
        var email = userName + "@aiursoft.com";
        var createResponse = await PostForm("/Users/Create", new Dictionary<string, string>
        {
            { "UserName", userName },
            { "DisplayName", "Test User" },
            { "Email", email },
            { "Password", "TestPassword123" }
        });
        AssertRedirect(createResponse, "/Users/Details/", exact: false);
        var userId = createResponse.Headers.Location?.OriginalString.Split("/").Last();
        Assert.IsNotNull(userId);

        // 3. Details
        var detailsResponse = await Http.GetAsync($"/Users/Details/{userId}");
        detailsResponse.EnsureSuccessStatusCode();
        var detailsHtml = await detailsResponse.Content.ReadAsStringAsync();
        Assert.Contains(userName, detailsHtml);

        // 4. Edit (GET)
        var editPageResponse = await Http.GetAsync($"/Users/Edit/{userId}");
        editPageResponse.EnsureSuccessStatusCode();

        // 5. Edit (POST)
        var newDisplayName = "Updated Test User";
        var editResponse = await PostForm($"/Users/Edit/{userId}", new Dictionary<string, string>
        {
            { "Id", userId },
            { "UserName", userName },
            { "Email", email },
            { "DisplayName", newDisplayName },
            { "AvatarUrl", User.DefaultAvatarPath }
        });
        AssertRedirect(editResponse, "/Users/Details/", exact: false);

        // 6. ManageRoles (POST)
        var manageRolesResponse = await PostForm($"/Users/ManageRoles/{userId}", new Dictionary<string, string>
        {
            { "id", userId },
            { "AllRoles[0].RoleName", "Administrators" },
            { "AllRoles[0].IsSelected", "true" }
        });
        AssertRedirect(manageRolesResponse, "/Users/Details/", exact: false);

        // 7. Delete (GET)
        var deletePageResponse = await Http.GetAsync($"/Users/Delete/{userId}");
        deletePageResponse.EnsureSuccessStatusCode();

        // 8. Delete (POST)
        var deleteResponse = await PostForm($"/Users/Delete/{userId}", new Dictionary<string, string>
        {
            { "id", userId }
        });
        AssertRedirect(deleteResponse, "/Users");
    }

    [TestMethod]
    public async Task TestCreateUserFailure()
    {
        await LoginAsAdmin();

        // Test with invalid model (empty password)
        var response = await PostForm("/Users/Create", new Dictionary<string, string>
        {
            { "UserName", "testuser" },
            { "Email", "test@test.com" },
            { "Password", "" }
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        // Should stay on the same page with validation error
    }

    [TestMethod]
    public async Task TestEditUserFailure()
    {
        await LoginAsAdmin();
        var (email, _) = await RegisterAndLoginAsync();
        var userInDb = await GetService<UserManager<User>>().FindByEmailAsync(email);
        var userId = userInDb!.Id;
        await LoginAsAdmin();

        // Test with invalid model (empty email)
        var response = await PostForm($"/Users/Edit/{userId}", new Dictionary<string, string>
        {
            { "Id", userId },
            { "UserName", "updated" },
            { "Email", "" },
            { "DisplayName", "Updated" }
        });

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task TestDetailsNotFound()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Users/Details/invalid-id");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestDetailsNullId()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Users/Details/");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestDeleteConfirmedNotFound()
    {
        await LoginAsAdmin();
        var response = await PostForm("/Users/Delete/invalid-id", new Dictionary<string, string>());
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestEditNotFound()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Users/Edit/invalid-id");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }

    [TestMethod]
    public async Task TestDeleteNotFound()
    {
        await LoginAsAdmin();
        var response = await Http.GetAsync("/Users/Delete/invalid-id");
        Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
    }
}
