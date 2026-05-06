using Aiursoft.HowToCookViewer.Entities;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Services;

/// <summary>
/// Resolves AI-translated recipe name/description strings for the current request culture.
/// Extracted as a scoped service so it can be shared by any controller that renders recipe cards.
/// </summary>
public class RecipeLocalizationService(
    TemplateDbContext db,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<(Dictionary<int, string> Names, Dictionary<int, string> Descriptions)>
        LoadLocalizedStringsAsync(IEnumerable<Recipe> recipes)
    {
        var list = recipes as List<Recipe> ?? recipes.ToList();
        if (list.Count == 0) return ([], []);

        var culture = httpContextAccessor.HttpContext?.Features
            .Get<IRequestCultureFeature>()
            ?.RequestCulture.Culture.Name ?? string.Empty;
        if (string.IsNullOrEmpty(culture)) return ([], []);

        var ids = list.Select(r => r.Id).ToList();
        var rows = await db.LocalizedRecipes
            .Where(lr => ids.Contains(lr.RecipeId) && lr.Culture == culture)
            .Select(lr => new { lr.RecipeId, lr.LocalizedName, lr.LocalizedDescription })
            .ToListAsync();

        var names = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.LocalizedName))
            .ToDictionary(r => r.RecipeId, r => r.LocalizedName);
        var descs = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.LocalizedDescription))
            .ToDictionary(r => r.RecipeId, r => r.LocalizedDescription);
        return (names, descs);
    }
}
