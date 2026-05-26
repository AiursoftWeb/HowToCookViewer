using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.IngredientsViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel() { PageTitle = "Ingredient Reverse Lookup"; }

    public List<IngredientGroupViewModel> Groups { get; set; } = [];
    public int GroupCount { get; set; }
    public int RawIngredientCount { get; set; }
    public HashSet<int> PreSelectedCanonicalIds { get; set; } = [];
}
