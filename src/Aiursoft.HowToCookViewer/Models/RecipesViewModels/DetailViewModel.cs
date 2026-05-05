using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.RecipesViewModels;

public class DetailViewModel : UiStackLayoutViewModel
{
    public DetailViewModel()
    {
        PageTitle = "Recipe";
    }

    public Recipe Recipe { get; set; } = null!;

    /// <summary>Pre-rendered HTML from the recipe's Markdown content.</summary>
    public string RenderedMarkdown { get; set; } = string.Empty;
}
