using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.HowToCookViewer.Entities;

[ExcludeFromCodeCoverage]
public class RecipeImage
{
    [Key]
    public int Id { get; set; }

    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    /// <summary>
    /// Logical path inside the StorageService Workspace,
    /// e.g. "recipe-images/3f2a...uuid....jpg".
    /// Pass directly to <c>StorageService.RelativePathToInternetUrl()</c>.
    /// </summary>
    [MaxLength(300)]
    public required string LogicalPath { get; set; }

    /// <summary>True for the first (cover) image of the recipe.</summary>
    public bool IsCover { get; set; }
}
