using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.HomeViewModels;

public class IndexViewModel : UiStackLayoutViewModel
{
    public IndexViewModel()
    {
        PageTitle = "Home";
    }

    public int TotalRecipes { get; set; }

    public List<Recipe> FeaturedRecipes { get; set; } = [];

    public Dictionary<int, int> LikeCounts { get; set; } = [];

    public Dictionary<int, string> LocalizedNames { get; set; } = [];

    public Dictionary<int, string> LocalizedDescriptions { get; set; } = [];

    public bool ShowVoxihostAd { get; set; }
}
