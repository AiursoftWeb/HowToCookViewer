using System.Net;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Configuration;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class CommentsRateLimitTests : TestBase
{
    [TestMethod]
    public async Task TestCommentRateLimit()
    {
        // 1. Register and login
        await RegisterAndLoginAsync();

        // 2. Create a recipe to comment on
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.Recipes.Add(new Recipe
            {
                Name = "Test Recipe",
                Category = "Test",
                FilePath = "test-recipe.md"
            });
            await db.SaveChangesAsync();
        }
        
        var recipeId = 1; // Assuming it's the first recipe

        // 3. Get the limit (default should be 10)
        var globalSettingsService = GetService<GlobalSettingsService>();
        var limit = await globalSettingsService.GetIntSettingAsync(SettingsMap.MaxCommentsPerDayPerUser);
        Assert.AreEqual(10, limit);

        // 4. Post comments up to the limit
        for (int i = 0; i < limit; i++)
        {
            var response = await PostForm("/Comments/Post", new Dictionary<string, string>
            {
                { "recipeId", recipeId.ToString() },
                { "content", $"Comment {i}" }
            }, tokenUrl: $"/Recipes/Detail/{recipeId}");
            AssertRedirect(response, $"/Recipes/Detail/{recipeId}#comments");
        }

        // 5. Post one more comment and expect 429
        var limitExceededResponse = await PostForm("/Comments/Post", new Dictionary<string, string>
        {
            { "recipeId", recipeId.ToString() },
            { "content", "One too many" }
        }, tokenUrl: $"/Recipes/Detail/{recipeId}");

        Assert.AreEqual((HttpStatusCode)429, limitExceededResponse.StatusCode);
    }
    
    [TestMethod]
    public async Task TestCommentRateLimitWithCustomValue()
    {
        // 1. Register and login
        await RegisterAndLoginAsync();

        // 2. Create a recipe to comment on
        using (var scope = Server!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            db.Recipes.Add(new Recipe
            {
                Name = "Test Recipe 2",
                Category = "Test",
                FilePath = "test-recipe-2.md"
            });
            await db.SaveChangesAsync();
        }
        
        var recipeId = 1; 

        // 3. Change the limit to 2
        var globalSettingsService = GetService<GlobalSettingsService>();
        await globalSettingsService.UpdateSettingAsync(SettingsMap.MaxCommentsPerDayPerUser, "2");

        // 4. Post 2 comments
        for (int i = 0; i < 2; i++)
        {
            var response = await PostForm("/Comments/Post", new Dictionary<string, string>
            {
                { "recipeId", recipeId.ToString() },
                { "content", $"Comment {i}" }
            }, tokenUrl: $"/Recipes/Detail/{recipeId}");
            AssertRedirect(response, $"/Recipes/Detail/{recipeId}#comments");
        }

        // 5. Post one more comment and expect 429
        var limitExceededResponse = await PostForm("/Comments/Post", new Dictionary<string, string>
        {
            { "recipeId", recipeId.ToString() },
            { "content", "One too many" }
        }, tokenUrl: $"/Recipes/Detail/{recipeId}");

        Assert.AreEqual((HttpStatusCode)429, limitExceededResponse.StatusCode);
    }
}
