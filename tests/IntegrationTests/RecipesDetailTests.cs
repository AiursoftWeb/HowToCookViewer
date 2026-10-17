using System.Net;
using System.Text.RegularExpressions;
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
        Assert.Contains("/ 5", html);
        Assert.Contains("difficulty-meter", html);
        Assert.Contains("difficulty-icon", html);
        Assert.Contains("Expert", html); // Difficulty 5 should be "Expert" (index 4)
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
            Difficulty = 5,
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
        Assert.Contains("5", html);
        Assert.Contains("Expert", html); // Difficulty 5 should be "Expert" (index 4)
    }

    [TestMethod]
    public async Task RecipeStepsPreserveDiagramsCodeAndEscapedHtml()
    {
        var db = GetService<TemplateDbContext>();
        var recipe = new Recipe
        {
            Name = "Diagram recipe",
            Category = "vegetable_dish",
            FilePath = "dishes/vegetable_dish/diagram.md",
            Description = "A recipe with illustrated steps.",
            Ingredients = "Vegetables",
            Steps = "```mermaid\ngraph TD; Cook-->Serve;\n```\n\n```html\n<script>window.recipeCode=1</script>\n```\n\n<script>window.recipeXss=1</script>"
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        var response = await Http.GetAsync($"/Recipes/Detail/{recipe.Id}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var diagram = Regex.Match(html, "<(?:div|pre) class=\"mermaid\">(.*?)</(?:div|pre)>", RegexOptions.Singleline);
        Assert.IsTrue(diagram.Success);
        Assert.Contains("graph TD; Cook-->Serve;", WebUtility.HtmlDecode(diagram.Groups[1].Value));
        Assert.Contains("language-html", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>window.recipeCode", html);
        Assert.DoesNotContain("<script>window.recipeXss", html);
    }

}
