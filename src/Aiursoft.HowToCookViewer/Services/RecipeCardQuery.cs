using Aiursoft.HowToCookViewer.Entities;

namespace Aiursoft.HowToCookViewer.Services;

public static class RecipeCardQuery
{
    // Card views never need recipe bodies, embeddings or translated recipe collections.
    public static IQueryable<Recipe> SelectCards(this IQueryable<Recipe> query) => query.Select(r => new Recipe
    {
        Id = r.Id,
        Name = r.Name,
        Category = r.Category,
        FilePath = r.FilePath,
        Description = r.Description,
        Difficulty = r.Difficulty,
        Calories = r.Calories,
        Images = r.Images.Where(i => i.IsCover).ToList()
    });
}
