using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.HowToCookViewer.Entities;

[ExcludeFromCodeCoverage]
public class Recipe
{
    [Key]
    public int Id { get; set; }

    /// <summary>Display name, e.g. "西红柿炒鸡蛋"</summary>
    [MaxLength(200)]
    public required string Name { get; set; }

    /// <summary>Top-level dishes subfolder, e.g. "vegetable_dish"</summary>
    [MaxLength(100)]
    public required string Category { get; set; }

    /// <summary>
    /// Non-null only for folder-style recipes (multiple variants under one directory),
    /// e.g. "鸡蛋羹". Null for single-file recipes.
    /// </summary>
    [MaxLength(200)]
    public string? GroupName { get; set; }

    /// <summary>
    /// Repo-relative path to the .md file, e.g.
    /// "dishes/vegetable_dish/西红柿炒鸡蛋.md".
    /// Used as the natural key for upsert during sync.
    /// </summary>
    [MaxLength(500)]
    public required string FilePath { get; set; }

    /// <summary>Cooking difficulty parsed from ★ count (1–8).</summary>
    public int Difficulty { get; set; }

    /// <summary>Introductory paragraph before the difficulty line.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Raw Markdown block for "## 必备原料和工具".</summary>
    public string Ingredients { get; set; } = string.Empty;

    /// <summary>Raw Markdown block for "## 计算".</summary>
    public string Calculation { get; set; } = string.Empty;

    /// <summary>Raw Markdown block for "## 操作".</summary>
    public string Steps { get; set; } = string.Empty;

    /// <summary>Raw Markdown block for "## 附加内容".</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>
    /// Last-commit timestamp of the .md file obtained from `git log`.
    /// Used to detect changes during incremental sync.
    /// </summary>
    public DateTime FileLastModified { get; set; }

    /// <summary>
    /// UTC timestamp of the last successful ingredient extraction.
    /// Used to detect changes during incremental extraction.
    /// </summary>
    public DateTime LastIngredientExtractedAt { get; set; } = DateTime.MinValue;

    public ICollection<Ingredient> ConsumedIngredients { get; set; } = [];

    public ICollection<RecipeImage> Images { get; set; } = [];
    public ICollection<LocalizedRecipe> LocalizedRecipes { get; set; } = [];

    /// <summary>Indicates if the recipe was deleted from the upstream repository.</summary>
    public bool IsDeleted { get; set; }
}
