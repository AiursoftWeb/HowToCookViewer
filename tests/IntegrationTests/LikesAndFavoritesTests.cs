using System.Net;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class LikesAndFavoritesTests : TestBase
{
    [TestMethod]
    public async Task ToggleLike_Unauthenticated_Redirects()
    {
        var response = await Http.PostAsync("/Likes/Toggle?recipeId=1", new FormUrlEncodedContent([]));
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
    }

    [TestMethod]
    public async Task ToggleLike_Authenticated_TogglesSuccessfully()
    {
        await RegisterAndLoginAsync();

        int recipeId;
        using (var scope = Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var recipe = await db.Recipes.FirstOrDefaultAsync();
            if (recipe == null)
            {
                recipe = new Recipe
                {
                    Name = "Test Recipe",
                    Category = "Meat",
                    FilePath = "meat/test.md",
                    };
                db.Recipes.Add(recipe);
                await db.SaveChangesAsync();
            }
            recipeId = recipe.Id;
        }

        var response = await PostForm($"/Likes/Toggle?recipeId={recipeId}", new Dictionary<string, string>());
        AssertRedirect(response, $"/Recipes/Detail/{recipeId}");

        using (var scope = Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var like = await db.RecipeLikes.FirstOrDefaultAsync(l => l.RecipeId == recipeId);
            Assert.IsNotNull(like);
        }

        // Toggle again to remove like
        var response2 = await PostForm($"/Likes/Toggle?recipeId={recipeId}", new Dictionary<string, string>());
        AssertRedirect(response2, $"/Recipes/Detail/{recipeId}");

        using (var scope = Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var like = await db.RecipeLikes.FirstOrDefaultAsync(l => l.RecipeId == recipeId);
            Assert.IsNull(like);
        }
    }

    [TestMethod]
    public async Task ToggleFavorite_Authenticated_TogglesSuccessfully()
    {
        await RegisterAndLoginAsync();

        int recipeId;
        using (var scope = Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var recipe = await db.Recipes.FirstOrDefaultAsync();
            if (recipe == null)
            {
                recipe = new Recipe
                {
                    Name = "Test Recipe 2",
                    Category = "Vegetable",
                    FilePath = "veg/test.md",
                    };
                db.Recipes.Add(recipe);
                await db.SaveChangesAsync();
            }
            recipeId = recipe.Id;
        }

        var response = await PostForm($"/Favorites/Toggle?recipeId={recipeId}", new Dictionary<string, string>());
        AssertRedirect(response, $"/Recipes/Detail/{recipeId}");

        // Favorite again to remove
        var response2 = await PostForm($"/Favorites/Toggle?recipeId={recipeId}", new Dictionary<string, string>());
        AssertRedirect(response2, $"/Recipes/Detail/{recipeId}");
    }

    [TestMethod]
    public async Task TipDetail_ReturnsSuccessOrNotFound()
    {
        int tipId;
        using (var scope = Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var tip = await db.Tips.FirstOrDefaultAsync();
            if (tip == null)
            {
                tip = new Tip
                {
                    Title = "Cooking Tip",
                    Content = "Always salt your water",
                    Category = "General",
                    FilePath = "tips/salt.md"
                };
                db.Tips.Add(tip);
                await db.SaveChangesAsync();
            }
            tipId = tip.Id;
        }

        var response = await Http.GetAsync($"/Tips/Detail/{tipId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var notFoundResponse = await Http.GetAsync("/Tips/Detail/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
    }
}
