using System.Net;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class RecipesAndDashboardTests : TestBase
{
    private async Task<int> EnsureRecipeAsync()
    {
        using var scope = Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        var recipe = await db.Recipes.FirstOrDefaultAsync();
        if (recipe == null)
        {
            recipe = new Recipe
            {
                Name = "Kung Pao Chicken",
                Category = "Meat",
                FilePath = "meat/kung_pao.md",
                Description = "A spicy stir-fried Chinese dish",
                Ingredients = "Chicken, Peanuts, Vegetables",
                Calculation = "1 serving",
                Steps = "Cook everything",
                Notes = "Careful with chili",
                Difficulty = 2
            };
            db.Recipes.Add(recipe);
            await db.SaveChangesAsync();
        }
        return recipe.Id;
    }

    [TestMethod]
    public async Task Recipes_Index_ReturnsSuccess()
    {
        await EnsureRecipeAsync();

        var response = await Http.GetAsync("/Recipes");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var responseFiltered = await Http.GetAsync("/Recipes?category=Meat&sortBy=likes&difficulty=2");
        Assert.AreEqual(HttpStatusCode.OK, responseFiltered.StatusCode);

        var responseDifficulty = await Http.GetAsync("/Recipes?sortBy=difficulty");
        Assert.AreEqual(HttpStatusCode.OK, responseDifficulty.StatusCode);

        var responseCalories = await Http.GetAsync("/Recipes?sortBy=calories");
        Assert.AreEqual(HttpStatusCode.OK, responseCalories.StatusCode);
    }

    [TestMethod]
    public async Task Recipes_LoadMore_ReturnsSuccess()
    {
        await EnsureRecipeAsync();

        var response = await Http.GetAsync("/Recipes/LoadMore?page=2");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var responseWithFilter = await Http.GetAsync("/Recipes/LoadMore?category=Meat&sortBy=difficulty&page=2");
        Assert.AreEqual(HttpStatusCode.OK, responseWithFilter.StatusCode);
    }

    [TestMethod]
    public async Task Recipes_Random_RedirectsToDetail()
    {
        await EnsureRecipeAsync();

        var response = await Http.GetAsync("/Recipes/Random");
        Assert.AreEqual(HttpStatusCode.Found, response.StatusCode);
        Assert.IsTrue(response.Headers.Location?.ToString().Contains("/Recipes/Detail/") == true);
    }

    [TestMethod]
    public async Task Recipes_Detail_ReturnsSuccessAndNotFound()
    {
        var recipeId = await EnsureRecipeAsync();

        var response = await Http.GetAsync($"/Recipes/Detail/{recipeId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var notFound = await Http.GetAsync("/Recipes/Detail/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [TestMethod]
    public async Task Recipes_Similar_ReturnsSuccessAndNotFound()
    {
        var recipeId = await EnsureRecipeAsync();

        var response = await Http.GetAsync($"/Recipes/Similar/{recipeId}");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var notFound = await Http.GetAsync("/Recipes/Similar/999999");
        Assert.AreEqual(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [TestMethod]
    public async Task Dashboard_Index_ReturnsSuccess()
    {
        await EnsureRecipeAsync();

        var response = await Http.GetAsync("/Dashboard");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        var responseSearch = await Http.GetAsync("/Dashboard?q=Chicken");
        Assert.AreEqual(HttpStatusCode.OK, responseSearch.StatusCode);
    }

    [TestMethod]
    public async Task Dashboard_TopRecipesLoadMore_ReturnsSuccess()
    {
        await EnsureRecipeAsync();

        var response = await Http.GetAsync("/Dashboard/TopRecipesLoadMore?page=2");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public async Task Comments_PostAndDelete_WorksSuccessfully()
    {
        var recipeId = await EnsureRecipeAsync();
        await RegisterAndLoginAsync();

        var postResp = await PostForm($"/Comments/Post?recipeId={recipeId}", new Dictionary<string, string>
        {
            { "content", "This recipe is amazing!" }
        });
        AssertRedirect(postResp, $"/Recipes/Detail/{recipeId}#comments");

        int commentId;
        using (var scope = Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
            var comment = await db.RecipeComments.OrderByDescending(c => c.Id).FirstAsync(c => c.RecipeId == recipeId && c.Content == "This recipe is amazing!");
            commentId = comment.Id;
        }

        // Reply to comment
        var replyResp = await PostForm($"/Comments/Post?recipeId={recipeId}&parentCommentId={commentId}", new Dictionary<string, string>
        {
            { "content", "Glad you liked it!" }
        });
        AssertRedirect(replyResp, $"/Recipes/Detail/{recipeId}#comments");

        // Delete comment
        var deleteResp = await PostForm($"/Comments/Delete?commentId={commentId}", new Dictionary<string, string>());
        AssertRedirect(deleteResp, $"/Recipes/Detail/{recipeId}#comments");
    }
}
