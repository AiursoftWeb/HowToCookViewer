using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models.RecipesViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using DashboardIndexViewModel = Aiursoft.HowToCookViewer.Models.DashboardViewModels.IndexViewModel;

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
                baseQuery, db, q, page, DashboardIndexViewModel.PageSize);
        }
        else
        {
            totalResults = totalRecipes;
            results = await baseQuery
                .Include(r => r.Images)
                .OrderByDescending(r => r.Images.Any())
                .ThenByDescending(r => db.RecipeLikes.Count(l => l.RecipeId == r.Id))
                .ThenByDescending(r => db.RecipeFavorites.Count(f => f.RecipeId == r.Id))
                .ThenBy(r => r.Name)
                .Skip((page - 1) * DashboardIndexViewModel.PageSize)
                .Take(DashboardIndexViewModel.PageSize)
                .ToListAsync();
        }

        var (localizedNames, localizedDescs) = await recipeLocalization.LoadLocalizedStringsAsync(results);

        var categoryNames = results
            .Select(r => r.Category)
            .Distinct()
            .ToDictionary(
                cat => cat,
                cat => categoryLocalizer[RecipesController.CategoryLocalizerKeys.TryGetValue(cat, out var key) ? key : cat].Value);

        // ── Top-liked recipes with images (for the landing page grid) ──────
        var topQuery = TopLikedWithImagesQuery();
        var topTotalWithImages = await topQuery.CountAsync();
        var topRecipes = await topQuery
            .Include(r => r.Images)
            .Take(DashboardIndexViewModel.PageSize)
            .ToListAsync();
        var (topLocalizedNames, topLocalizedDescs) = await recipeLocalization.LoadLocalizedStringsAsync(topRecipes);

        return this.StackView(new DashboardIndexViewModel
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
            TopRecipes = topRecipes,
            TopLikeCounts = await LoadLikeCountsAsync(topRecipes),
            TopLocalizedNames = topLocalizedNames,
            TopLocalizedDescriptions = topLocalizedDescs,
            TopTotalWithImages = topTotalWithImages,
        });
    }

    [HttpGet]
    public async Task<IActionResult> TopRecipesLoadMore(int page = 2)
    {
        page = Math.Max(2, page);
        var query = TopLikedWithImagesQuery();
        var totalCount = await query.CountAsync();
        var recipes = await query
            .Include(r => r.Images)
            .Skip((page - 1) * DashboardIndexViewModel.PageSize)
            .Take(DashboardIndexViewModel.PageSize)
            .ToListAsync();

        var hasMore = page * DashboardIndexViewModel.PageSize < totalCount;
        Response.Headers["X-Has-More"] = hasMore ? "true" : "false";

        var (localizedNames, localizedDescs) = await recipeLocalization.LoadLocalizedStringsAsync(recipes);
        return PartialView("_RecipeCards", new RecipeCardsViewModel
        {
            Recipes = recipes,
            LikeCounts = await LoadLikeCountsAsync(recipes),
            LocalizedNames = localizedNames,
            LocalizedDescriptions = localizedDescs
        });
    }

    private IQueryable<Recipe> TopLikedWithImagesQuery() =>
        db.Recipes.AsNoTracking()
            .Where(r => r.Images.Any())
            .OrderByDescending(r => db.RecipeLikes.Count(l => l.RecipeId == r.Id))
            .ThenByDescending(r => db.RecipeFavorites.Count(f => f.RecipeId == r.Id))
            .ThenBy(r => r.Name);

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
