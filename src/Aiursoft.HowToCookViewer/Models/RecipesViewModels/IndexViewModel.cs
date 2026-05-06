using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.RecipesViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Recipes";
    }

    /// <summary>Null means "all categories".</summary>
    public string? Category { get; set; }

    /// <summary>Null means "all difficulties".</summary>
    public int? Difficulty { get; set; }

    /// <summary>Null means default (name) order. Values: likes_desc/asc, comments_desc/asc, favorites_desc/asc.</summary>
    public string? SortBy { get; set; }

    public string CategoryDisplayName { get; set; } = "全部菜谱";

    public List<Recipe> Recipes { get; set; } = [];

    public int TotalCount { get; set; }

    /// <summary>Maps RecipeId → like count for the current page of recipes.</summary>
    public Dictionary<int, int> LikeCounts { get; set; } = [];
}
