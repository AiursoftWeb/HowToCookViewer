using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.IngredientsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel() { PageTitle = "Ingredient Reverse Lookup"; }

    public List<Ingredient> AllIngredients { get; set; } = [];
    public int IngredientCount { get; set; }
    public HashSet<int> PreSelectedIds { get; set; } = [];
}
