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

    public bool IsFavorited { get; set; }

    public bool IsLiked { get; set; }

    public int LikeCount { get; set; }

    /// <summary>Direct link to edit this file on GitHub.</summary>
    public string? GitHubEditUrl { get; set; }

    /// <summary>Root-level comments with Replies pre-loaded.</summary>
    public List<RecipeComment> Comments { get; set; } = [];
}
