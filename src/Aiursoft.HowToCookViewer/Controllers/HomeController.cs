using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models.HomeViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Controllers;

[LimitPerMin]
public class HomeController(
    TemplateDbContext db,
    RecipeLocalizationService recipeLocalization,
    GlobalSettingsService globalSettings) : Controller
{
    public async Task<IActionResult> Index()
    {
        var totalRecipes = await db.Recipes.CountAsync();

        // Top liked recipe IDs (over-fetch to filter for those with a cover image)
        var topLiked = await db.RecipeLikes
            .GroupBy(l => l.RecipeId)
            .Select(g => new { RecipeId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(30)
            .ToListAsync();

        var candidateIds = topLiked.Select(x => x.RecipeId).ToList();
        var candidates = await db.Recipes
            .Include(r => r.Images)
            .Where(r => candidateIds.Contains(r.Id) && r.Images.Any(i => i.IsCover))
            .ToListAsync();

        var featured = candidates
            .OrderByDescending(r => topLiked.FirstOrDefault(x => x.RecipeId == r.Id)?.Count ?? 0)
            .Take(8)
            .ToList();

        // If fewer than 8 liked recipes with images exist, fill up with the newest ones
        if (featured.Count < 8)
        {
            var existingIds = featured.Select(r => r.Id).ToHashSet();
            var fill = await db.Recipes
                .Include(r => r.Images)
                .Where(r => !existingIds.Contains(r.Id) && r.Images.Any(i => i.IsCover))
                .OrderByDescending(r => r.Id)
                .Take(8 - featured.Count)
                .ToListAsync();
            featured.AddRange(fill);
        }

        var (localizedNames, localizedDescs) = await recipeLocalization.LoadLocalizedStringsAsync(featured);
        var likeCounts = await LoadLikeCountsAsync(featured);

        var showVoxihostAd = await globalSettings.GetBoolSettingAsync(SettingsMap.ShowVoxihostAd);

        return this.SimpleView(new IndexViewModel
        {
            TotalRecipes = totalRecipes,
            FeaturedRecipes = featured,
            LikeCounts = likeCounts,
            LocalizedNames = localizedNames,
            LocalizedDescriptions = localizedDescs,
            ShowVoxihostAd = showVoxihostAd
        });
    }

    public IActionResult SelfHost()
    {
        return this.SimpleView(new SelfHostViewModel());
    }

    private async Task<Dictionary<int, int>> LoadLikeCountsAsync(List<Recipe> recipes)
    {
        if (recipes.Count == 0) return [];
        var ids = recipes.Select(r => r.Id).ToList();
        return await db.RecipeLikes
            .Where(l => ids.Contains(l.RecipeId))
            .GroupBy(l => l.RecipeId)
            .Select(g => new { RecipeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RecipeId, x => x.Count);
    }
}
