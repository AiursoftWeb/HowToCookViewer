using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class ExtractIngredientsJobTests : TestBase
{
    [TestMethod]
    public async Task TestIngredientSchema()
    {
        using var scope = Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();

        // 1. Create a recipe
        var recipe = new Recipe
        {
            Name = "Test Recipe",
            Category = "test",
            FilePath = "dishes/test/recipe.md",
            Description = "Test description",
            Ingredients = "Test ingredients"
        };
        db.Recipes.Add(recipe);
        await db.SaveChangesAsync();

        // 2. Create an ingredient
        var ingredient = new Ingredient
        {
            Name = "Tomato"
        };
        db.Ingredients.Add(ingredient);
        await db.SaveChangesAsync();

        // 3. Link them
        recipe.ConsumedIngredients.Add(ingredient);
        await db.SaveChangesAsync();

        // 4. Verify link
        var recipeFromDb = await db.Recipes
            .Include(r => r.ConsumedIngredients)
            .FirstAsync(r => r.Id == recipe.Id);

        Assert.AreEqual(1, recipeFromDb.ConsumedIngredients.Count);
        Assert.AreEqual("Tomato", recipeFromDb.ConsumedIngredients.First().Name);

        // 5. Verify reverse link
        var ingredientFromDb = await db.Ingredients
            .Include(i => i.Recipes)
            .FirstAsync(i => i.Id == ingredient.Id);

        Assert.AreEqual(1, ingredientFromDb.Recipes.Count);
        Assert.AreEqual("Test Recipe", ingredientFromDb.Recipes.First().Name);
    }
}
