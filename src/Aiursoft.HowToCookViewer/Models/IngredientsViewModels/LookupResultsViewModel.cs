using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models.RecipesViewModels;

namespace Aiursoft.HowToCookViewer.Models.IngredientsViewModels;

public class LookupResultsViewModel
{
    public RecipeCardsViewModel ExactMatches { get; set; } = new();
    public List<NearMatchViewModel> NearMatches { get; set; } = [];
}

public class NearMatchViewModel
{
    public Recipe Recipe { get; set; } = null!;
    public int MatchPercentage { get; set; }
    public string MissingIngredients { get; set; } = "";
    public int LikeCount { get; set; }
    public string LocalizedName { get; set; } = "";
    public string LocalizedDescription { get; set; } = "";
}
