using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Services;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class SimilarRecipesTests : TestBase
{
    [TestMethod]
    public async Task TestSimilarRecipesButtonVisibility()
    {
        // Arrange
        var db = GetService<TemplateDbContext>();
        var cache = GetService<RecipeEmbeddingCache>();
        
        // Clear existing recipes
        db.Recipes.RemoveRange(db.Recipes);
        await db.SaveChangesAsync();
        
        // Add two recipes
        var r1 = new Recipe
        {
            Name = "Recipe 1",
            Category = "vegetable_dish",
            FilePath = "dishes/vegetable_dish/r1.md",
            Embedding = new byte[4 * 4] // Mock embedding
        };
        var r2 = new Recipe
        {
            Name = "Recipe 2",
            Category = "vegetable_dish",
            FilePath = "dishes/vegetable_dish/r2.md"
            // Missing embedding
        };
        db.Recipes.AddRange(r1, r2);
        await db.SaveChangesAsync();

        // Reload cache
        await cache.LoadAsync(db);

        // Act - Button should NOT be visible because r2 is missing embedding
        var response = await Http.GetAsync($"/Recipes/Detail/{r1.Id}");
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.IsFalse(html.Contains("<a href=\"/Recipes/Similar/"), "Button should not be visible when some recipes are missing embeddings.");

        // Arrange - Add embedding to r2
        r2.Embedding = new byte[4 * 4];
        await db.SaveChangesAsync();
        await cache.LoadAsync(db);

        // Act - Button should NOW be visible
        response = await Http.GetAsync($"/Recipes/Detail/{r1.Id}");
        html = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.IsTrue(html.Contains("<a href=\"/Recipes/Similar/"), "Button should be visible when all recipes have embeddings.");
    }

    [TestMethod]
    public async Task TestSimilarRecipesEndpoint()
    {
        // Arrange
        var db = GetService<TemplateDbContext>();
        var cache = GetService<RecipeEmbeddingCache>();

        // Clear existing recipes
        db.Recipes.RemoveRange(db.Recipes);
        await db.SaveChangesAsync();

        // Create two vectors that are similar (all zeros except one)
        var v1 = new float[4];
        v1[0] = 1.0f;
        var v2 = new float[4];
        v2[0] = 0.9f;
        v2[1] = 0.1f;

        var b1 = new byte[v1.Length * 4];
        Buffer.BlockCopy(v1, 0, b1, 0, b1.Length);
        var b2 = new byte[v2.Length * 4];
        Buffer.BlockCopy(v2, 0, b2, 0, b2.Length);

        var r1 = new Recipe { Name = "R1", FilePath = "f1.md", Embedding = b1, Category = "vegetable_dish" };
        var r2 = new Recipe { Name = "R2", FilePath = "f2.md", Embedding = b2, Category = "vegetable_dish" };
        db.Recipes.AddRange(r1, r2);
        await db.SaveChangesAsync();
        await cache.LoadAsync(db);

        // Act
        var response = await Http.GetAsync($"/Recipes/Similar/{r1.Id}");
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("R2", html);
    }
}
