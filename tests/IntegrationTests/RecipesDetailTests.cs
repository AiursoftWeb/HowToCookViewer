using Aiursoft.HowToCookViewer.Entities;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class RecipesDetailTests : TestBase
{
    [TestMethod]
    public async Task GetRecipeDetailWithDifficulty()
    {
        // Arrange
        var db = GetService<TemplateDbContext>();
        var recipe = new Recipe
        {
            Name = "Test Recipe",
            Category = "vegetable_dish",
            FilePath = "dishes/vegetable_dish/test.md",
            Difficulty = 5,
            Description = "Test Description",
            Ingredients = "Test Ingredients",
            Steps = "Test Steps"
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        // Act
        var response = await Http.GetAsync($"/Recipes/Detail/{recipe.Id}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("Test Recipe", html);
        Assert.Contains("Difficulty", html);
        Assert.Contains("5", html);
        Assert.Contains("/ 8", html);
        Assert.Contains("difficulty-meter", html);
        Assert.Contains("difficulty-icon", html);
        Assert.Contains("Intermediate", html); // Difficulty 5 should be "Intermediate" (index 4)
    }

    [TestMethod]
    public async Task GetRecipeDetailWithExtremeDifficulty()
    {
        // Arrange
        var db = GetService<TemplateDbContext>();
        var recipe = new Recipe
        {
            Name = "Expert Recipe",
            Category = "meat_dish",
            FilePath = "dishes/meat_dish/expert.md",
            Difficulty = 8,
            Description = "Hard Description",
            Ingredients = "Hard Ingredients",
            Steps = "Hard Steps"
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        // Act
        var response = await Http.GetAsync($"/Recipes/Detail/{recipe.Id}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("Expert Recipe", html);
        Assert.Contains("8", html);
        Assert.Contains("Legendary", html); // Difficulty 8 should be "Legendary" (index 7)
    }
}
