using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.RecipesViewModels;

public class SimilarViewModel : UiStackLayoutViewModel
{
    public SimilarViewModel()
    {
        PageTitle = "Similar Recipes";
    }

    public Recipe SourceRecipe { get; set; } = null!;
    public string CategoryDisplayName { get; set; } = string.Empty;
    public List<Recipe> SimilarRecipes { get; set; } = [];
    public Dictionary<int, int> LikeCounts { get; set; } = [];
    public Dictionary<int, string> LocalizedNames { get; set; } = [];
    public Dictionary<int, string> LocalizedDescriptions { get; set; } = [];
}
