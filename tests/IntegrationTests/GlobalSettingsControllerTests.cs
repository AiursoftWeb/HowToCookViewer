using Aiursoft.HowToCookViewer.Configuration;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class GlobalSettingsControllerTests : TestBase
{
    [TestMethod]
    public async Task TestGlobalSettingsWorkflow()
    {
        await LoginAsAdmin();

        // 1. Index
        var indexResponse = await Http.GetAsync("/GlobalSettings/Index");
        indexResponse.EnsureSuccessStatusCode();
        var indexHtml = await indexResponse.Content.ReadAsStringAsync();
        Assert.Contains("Global Settings", indexHtml);
        Assert.Contains(SettingsMap.ProjectName, indexHtml);

        // 2. Edit (POST)
        var newProjectName = "My New Project " + Guid.NewGuid();
        var editResponse = await PostForm("/GlobalSettings/Edit", new Dictionary<string, string>
        {
            { "Key", SettingsMap.ProjectName },
            { "Value", newProjectName }
        });
        AssertRedirect(editResponse, "/GlobalSettings");

        // 3. Verify Edit
        var indexResponse2 = await Http.GetAsync("/GlobalSettings/Index");
        var indexHtml2 = await indexResponse2.Content.ReadAsStringAsync();
        Assert.Contains(newProjectName, indexHtml2);

        // 4. Edit (invalid key)
        var invalidEditResponse = await PostForm("/GlobalSettings/Edit", new Dictionary<string, string>
        {
            { "Key", "InvalidKey" },
            { "Value", "SomeValue" }
        });
        AssertRedirect(invalidEditResponse, "/GlobalSettings");
    }

    [TestMethod]
    public async Task TestEditInvalidModel()
    {
        await LoginAsAdmin();
        // Missing Key
        var response = await PostForm("/GlobalSettings/Edit", new Dictionary<string, string>
        {
            { "Value", "SomeValue" }
        });
        AssertRedirect(response, "/GlobalSettings");
    }
}
