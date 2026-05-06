using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.HowToCookViewer.Entities;

[ExcludeFromCodeCoverage]
public class LocalizedRecipe
{
    [Key]
    public int Id { get; set; }

    public int RecipeId { get; set; }
    public Recipe Recipe { get; set; } = null!;

    /// <summary>BCP-47 culture tag, e.g. "en-US", "ja-JP", "ko-KR".</summary>
    [MaxLength(20)]
    public required string Culture { get; set; }

    [MaxLength(200)]
    public string LocalizedName { get; set; } = string.Empty;

    public string LocalizedDescription { get; set; } = string.Empty;

    public string LocalizedIngredients { get; set; } = string.Empty;

    public string LocalizedCalculation { get; set; } = string.Empty;

    public string LocalizedSteps { get; set; } = string.Empty;

    public string LocalizedNotes { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the last successful AI translation.</summary>
    public DateTime LastLocalizedAt { get; set; } = DateTime.UtcNow;
}
