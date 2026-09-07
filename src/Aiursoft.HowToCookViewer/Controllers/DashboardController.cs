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

public class DashboardController(
    TemplateDbContext db,
    RecipeLocalizationService recipeLocalization,
    IStringLocalizer<RecipesController> categoryLocalizer,
    RecipeVectorSearchService vectorSearch) : Controller
{
    private const int MaxQueryLength = 40;

    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksIcon = "home",
        CascadedLinksOrder = 1,
        LinkText = "Index",
        LinkOrder = 1)]
    [LimitPerMin(8)]
    [ServiceFilter(typeof(SearchRequestFilter))]
    public async Task<IActionResult> Index(string? q, int page = 1, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 1, int.MaxValue / DashboardIndexViewModel.PageSize);

        var totalRecipes = await db.Recipes.CountAsync(ct);
        var baseQuery = db.Recipes.AsNoTracking();

        List<Recipe> results;
        int totalResults;
        var usedAi = false;

        if (!string.IsNullOrWhiteSpace(q))
        {
            // Truncate query to max allowed length.
            if (q.Length > MaxQueryLength)
            {
                q = q[..MaxQueryLength];
            }

            var aiResult = await vectorSearch.SearchAsync(
                baseQuery, q, page, DashboardIndexViewModel.PageSize, ct);
            if (aiResult.UsedAi)
            {
                usedAi = true;
                (results, totalResults) = (aiResult.Results, aiResult.TotalCount);
            }
            else
            {
                (results, totalResults) = await RecipeSearchService.SearchAsync(
                    baseQuery, db, q, page, DashboardIndexViewModel.PageSize, ct);
            }
        }
        else
        {
            totalResults = totalRecipes;
            results = await baseQuery
                .OrderByDescending(r => r.Images.Any())
                .ThenByDescending(r => db.RecipeLikes.Count(l => l.RecipeId == r.Id))
                .ThenByDescending(r => db.RecipeFavorites.Count(f => f.RecipeId == r.Id))
                .ThenBy(r => r.Name)
                .Skip((page - 1) * DashboardIndexViewModel.PageSize)
                .Take(DashboardIndexViewModel.PageSize)
                .SelectCards()
                .ToListAsync(ct);
        }

        var (localizedNames, localizedDescs) = await recipeLocalization.LoadLocalizedStringsAsync(results, ct);

        var categoryNames = results
            .Select(r => r.Category)
            .Distinct()
            .ToDictionary(
                cat => cat,
                cat => categoryLocalizer[RecipesController.CategoryLocalizerKeys.TryGetValue(cat, out var key) ? key : cat].Value);

        // ── Top-liked recipes with images (for the landing page grid) ──────
        var topQuery = TopLikedWithImagesQuery();
        var topTotalWithImages = string.IsNullOrWhiteSpace(q) ? await topQuery.CountAsync(ct) : 0;
        var topRecipes = string.IsNullOrWhiteSpace(q) ? await topQuery
            .Take(DashboardIndexViewModel.PageSize)
            .SelectCards()
            .ToListAsync(ct) : [];
        var (topLocalizedNames, topLocalizedDescs) = await recipeLocalization.LoadLocalizedStringsAsync(topRecipes, ct);

        return this.StackView(new DashboardIndexViewModel
        {
            Query = q,
            Page = page,
            TotalResults = totalResults,
            TotalRecipes = totalRecipes,
            Results = results,
            LikeCounts = await LoadLikeCountsAsync(results, ct),
            LocalizedNames = localizedNames,
            UsedAiSearch = usedAi,
            LocalizedDescriptions = localizedDescs,
            CategoryDisplayNames = categoryNames,
            TopRecipes = topRecipes,
            TopLikeCounts = await LoadLikeCountsAsync(topRecipes, ct),
            TopLocalizedNames = topLocalizedNames,
            TopLocalizedDescriptions = topLocalizedDescs,
            TopTotalWithImages = topTotalWithImages,
        });
    }

    [HttpGet]
    [LimitPerMin]
    public async Task<IActionResult> TopRecipesLoadMore(int page = 2, CancellationToken ct = default)
    {
        page = Math.Clamp(page, 2, int.MaxValue / DashboardIndexViewModel.PageSize);
        var query = TopLikedWithImagesQuery();
        var totalCount = await query.CountAsync(ct);
        var recipes = await query
            .Skip((page - 1) * DashboardIndexViewModel.PageSize)
            .Take(DashboardIndexViewModel.PageSize)
            .SelectCards()
            .ToListAsync(ct);

        var hasMore = page * DashboardIndexViewModel.PageSize < totalCount;
        Response.Headers["X-Has-More"] = hasMore ? "true" : "false";

        var (localizedNames, localizedDescs) = await recipeLocalization.LoadLocalizedStringsAsync(recipes, ct);
        return PartialView("_RecipeCards", new RecipeCardsViewModel
        {
            Recipes = recipes,
            LikeCounts = await LoadLikeCountsAsync(recipes, ct),
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

    private async Task<Dictionary<int, int>> LoadLikeCountsAsync(List<Recipe> recipes, CancellationToken ct)
    {
        if (recipes.Count == 0) return [];
        var ids = recipes.Select(r => r.Id).ToList();
        return await db.RecipeLikes
            .Where(l => ids.Contains(l.RecipeId))
            .GroupBy(l => l.RecipeId)
            .Select(g => new { RecipeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RecipeId, x => x.Count, ct);
    }
}
