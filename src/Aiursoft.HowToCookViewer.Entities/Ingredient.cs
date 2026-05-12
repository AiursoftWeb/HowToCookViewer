using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Entities;

[ExcludeFromCodeCoverage]
[Index(nameof(Name), IsUnique = true)]
public class Ingredient
{
    [Key]
    public int Id { get; set; }

    [MaxLength(100)]
    public required string Name { get; set; }

    public ICollection<Recipe> Recipes { get; set; } = [];
}
