using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aiursoft.HowToCookViewer.Entities;

public class RecipeFavorite
{
    [MaxLength(450)]
    public required string UserId { get; set; }

    public int RecipeId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public User User { get; set; } = null!;

    [ForeignKey(nameof(RecipeId))]
    public Recipe Recipe { get; set; } = null!;
}
