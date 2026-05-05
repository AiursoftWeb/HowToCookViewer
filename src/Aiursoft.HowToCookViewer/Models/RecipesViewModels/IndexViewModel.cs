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

    public string CategoryDisplayName { get; set; } = "全部菜谱";

    public List<Recipe> Recipes { get; set; } = [];

    public int TotalCount { get; set; }
}
