using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.HowToCookViewer.Entities;

[ExcludeFromCodeCoverage]
public class Tip
{
    [Key]
    public int Id { get; set; }

    /// <summary>File name without extension, e.g. "油温判断技巧"</summary>
    [MaxLength(200)]
    public required string Title { get; set; }

    /// <summary>Subdirectory under tips/, e.g. "advanced", "learn", or "root" for top-level files.</summary>
    [MaxLength(100)]
    public required string Category { get; set; }

    /// <summary>Repo-relative path, e.g. "tips/advanced/油温判断技巧.md". Used as natural key for upsert.</summary>
    [MaxLength(500)]
    public required string FilePath { get; set; }

    /// <summary>Full Markdown content of the tip file.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Last-commit timestamp from git log.</summary>
    public DateTime FileLastModified { get; set; }
}
