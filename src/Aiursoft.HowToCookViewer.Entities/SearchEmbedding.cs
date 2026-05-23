using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Entities;

[ExcludeFromCodeCoverage]
[Index(nameof(QueryText), IsUnique = true)]
public class SearchEmbedding
{
    [Key]
    public int Id { get; set; }

    [MaxLength(40)]
    public required string QueryText { get; set; }

    public byte[] Embedding { get; set; } = [];

    public DateTime CreatedAt { get; set; }

    public DateTime LastAccessedAt { get; set; }
}
