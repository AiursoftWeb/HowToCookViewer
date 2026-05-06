using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.HowToCookViewer.Entities;

[ExcludeFromCodeCoverage]
public class LocalizedTip
{
    [Key]
    public int Id { get; set; }

    public int TipId { get; set; }
    public Tip Tip { get; set; } = null!;

    /// <summary>BCP-47 culture tag, e.g. "en-US".</summary>
    [MaxLength(20)]
    public required string Culture { get; set; }

    [MaxLength(200)]
    public string LocalizedTitle { get; set; } = string.Empty;

    public string LocalizedContent { get; set; } = string.Empty;

    /// <summary>UTC timestamp of the last successful AI translation.</summary>
    public DateTime LastLocalizedAt { get; set; } = DateTime.UtcNow;
}
