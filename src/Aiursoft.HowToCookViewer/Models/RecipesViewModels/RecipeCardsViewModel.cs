using Aiursoft.HowToCookViewer.Entities;

namespace Aiursoft.HowToCookViewer.Models.RecipesViewModels;

/// <summary>Lightweight model for the _RecipeCards partial (infinite-scroll batches).</summary>
public class RecipeCardsViewModel
{
    public List<Recipe> Recipes { get; set; } = [];

    public Dictionary<int, int> LikeCounts { get; set; } = [];

    /// <summary>Localized name keyed by recipe ID (current request culture). Falls back to Recipe.Name.</summary>
    public Dictionary<int, string> LocalizedNames { get; set; } = [];

    /// <summary>Localized description keyed by recipe ID (current request culture). Falls back to Recipe.Description.</summary>
    public Dictionary<int, string> LocalizedDescriptions { get; set; } = [];
}
