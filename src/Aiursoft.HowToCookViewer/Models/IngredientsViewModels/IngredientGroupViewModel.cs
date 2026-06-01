using Aiursoft.HowToCookViewer.Entities;

namespace Aiursoft.HowToCookViewer.Models.IngredientsViewModels;

public class IngredientGroupViewModel
{
    public Ingredient Canonical { get; set; } = null!;

    public List<Ingredient> Aliases { get; set; } = [];


    public int DistinctRecipeCount { get; set; }

    public int GroupSize => Aliases.Count + 1;
}
