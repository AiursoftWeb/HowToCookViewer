using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.DashboardViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public const int PageSize = 12;

    public IndexViewModel()
    {
        PageTitle = "Recipe Search";
    }

    public string? Query { get; set; }

    public int Page { get; set; } = 1;

    public int TotalResults { get; set; }

    public int TotalRecipes { get; set; }

    public List<Recipe> Results { get; set; } = [];

    /// <summary>Maps RecipeId → like count for the current results page.</summary>
    public Dictionary<int, int> LikeCounts { get; set; } = [];
}
