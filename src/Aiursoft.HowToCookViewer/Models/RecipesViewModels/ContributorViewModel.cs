namespace Aiursoft.HowToCookViewer.Models.RecipesViewModels;

public class ContributorViewModel
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int CommitCount { get; set; }
    public string AvatarUrl { get; set; } = string.Empty;
    public string GitHubProfileUrl { get; set; } = string.Empty;
}
