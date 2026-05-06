using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.HowToCookViewer.Entities;

public class RecipeComment
{
    public int Id { get; set; }

    public int RecipeId { get; set; }

    [MaxLength(450)]
    public required string UserId { get; set; }

    /// <summary>Null = root comment on recipe. Non-null = reply to a root comment.</summary>
    public int? ParentCommentId { get; set; }

    [MaxLength(1000)]
    public required string Content { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(RecipeId))]
    public Recipe Recipe { get; set; } = null!;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(ParentCommentId))]
    public RecipeComment? ParentComment { get; set; }

    public List<RecipeComment> Replies { get; set; } = [];
}
