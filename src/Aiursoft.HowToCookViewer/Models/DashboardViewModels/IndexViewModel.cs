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

    /// <summary>Maps RecipeId → localized name for the current request culture.</summary>
    public Dictionary<int, string> LocalizedNames { get; set; } = [];

    /// <summary>Maps RecipeId → localized description for the current request culture.</summary>
    public Dictionary<int, string> LocalizedDescriptions { get; set; } = [];

    /// <summary>Maps category slug → localized display name for the current request culture.</summary>
    public Dictionary<string, string> CategoryDisplayNames { get; set; } = [];

    // ── Top-liked recipes (shown when no search query) ────────────────
    public List<Recipe> TopRecipes { get; set; } = [];
    public Dictionary<int, int> TopLikeCounts { get; set; } = [];
    public Dictionary<int, string> TopLocalizedNames { get; set; } = [];
    public Dictionary<int, string> TopLocalizedDescriptions { get; set; } = [];
    public int TopTotalWithImages { get; set; }
    public bool TopHasMore => TopTotalWithImages > PageSize;

    public bool UsedAiSearch { get; set; }
}
