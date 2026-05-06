using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models.DashboardViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.HowToCookViewer.Controllers;

[LimitPerMin]
public class DashboardController(
    TemplateDbContext db,
    RecipeLocalizationService recipeLocalization,
    IStringLocalizer<RecipesController> categoryLocalizer) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksIcon = "home",
        CascadedLinksOrder = 1,
        LinkText = "Index",
        LinkOrder = 1)]
    public async Task<IActionResult> Index(string? q, int page = 1)
    {
        page = Math.Max(1, page);

        var totalRecipes = await db.Recipes.CountAsync();
        var baseQuery = db.Recipes.AsNoTracking();

        List<Recipe> results;
        int totalResults;

        if (!string.IsNullOrWhiteSpace(q))
        {
            (results, totalResults) = await RecipeSearchService.SearchAsync(
                baseQuery, q, page, IndexViewModel.PageSize);
        }
        else
        {
            totalResults = totalRecipes;
            results = await baseQuery
                .Include(r => r.Images)
                .OrderBy(r => r.Name)
                .Skip((page - 1) * IndexViewModel.PageSize)
                .Take(IndexViewModel.PageSize)
                .ToListAsync();
        }

        var (localizedNames, localizedDescs) = await recipeLocalization.LoadLocalizedStringsAsync(results);

        // Build a localized category display name map for the result set
        var categoryNames = results
            .Select(r => r.Category)
            .Distinct()
            .ToDictionary(
                cat => cat,
                cat => categoryLocalizer[RecipesController.CategoryLocalizerKeys.TryGetValue(cat, out var key) ? key : cat].Value);

        return this.StackView(new IndexViewModel
        {
            Query = q,
            Page = page,
            TotalResults = totalResults,
            TotalRecipes = totalRecipes,
            Results = results,
            LikeCounts = await LoadLikeCountsAsync(results),
            LocalizedNames = localizedNames,
            LocalizedDescriptions = localizedDescs,
            CategoryDisplayNames = categoryNames,
        });
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
