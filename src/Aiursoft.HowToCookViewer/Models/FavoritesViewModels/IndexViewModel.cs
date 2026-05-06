using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.FavoritesViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "My Favorites";
    }

    public List<RecipeFavorite> Favorites { get; set; } = [];

    /// <summary>Maps RecipeId → localized name for the current request culture.</summary>
    public Dictionary<int, string> LocalizedNames { get; set; } = [];

    /// <summary>Maps RecipeId → localized description for the current request culture.</summary>
    public Dictionary<int, string> LocalizedDescriptions { get; set; } = [];
}
