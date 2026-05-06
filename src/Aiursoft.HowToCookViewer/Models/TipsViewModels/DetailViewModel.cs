using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.TipsViewModels;

public class DetailViewModel : UiStackLayoutViewModel
{
    public DetailViewModel()
    {
        PageTitle = "Tip";
    }

    public Tip Tip { get; set; } = null!;

    public string RenderedContent { get; set; } = string.Empty;

    public string DisplayTitle { get; set; } = string.Empty;

    /// <summary>Direct link to edit this file on GitHub.</summary>
    public string? GitHubEditUrl { get; set; }
}
