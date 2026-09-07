using Aiursoft.HowToCookViewer.Entities;
using IngredientIndexVm = Aiursoft.HowToCookViewer.Models.IngredientsViewModels.IndexViewModel;
using Aiursoft.HowToCookViewer.Models.IngredientsViewModels;
using Aiursoft.HowToCookViewer.Models.RecipesViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Localization;

namespace Aiursoft.HowToCookViewer.Controllers;

public class IngredientsController(
    TemplateDbContext db,
    RecipeLocalizationService recipeLocalization,
    IStringLocalizer<IngredientsController> localizer,
    IngredientGroupService groupService,
    GlobalSettingsService settingsService) : Controller
{
    [ExcludeFromCodeCoverage]
    // ReSharper disable once UnusedMember.Local
    private void _useless_for_localizer()
    {
        _ = localizer["Ingredient Reverse Lookup"];
    }

    [RenderInNavBar(
        NavGroupName = "Features",
        NavGroupOrder = 1,
        CascadedLinksGroupName = "Home",
        CascadedLinksIcon = "home",
        CascadedLinksOrder = 1,
        LinkText = "Ingredient Reverse Lookup",
        LinkOrder = 2)]
    public async Task<IActionResult> Index()
    {
        var groups = await groupService.GetGroupsAsync(db, settingsService);

        var preSelectedIds = groups
            .Take(20)
            .Select(g => g.Canonical.Id)
            .ToHashSet();

        return this.StackView(new IngredientIndexVm
        {
            Groups = [.. groups],
            GroupCount = groups.Count,
            RawIngredientCount = groups.Sum(g => g.GroupSize),
            PreSelectedCanonicalIds = preSelectedIds
        });
    }

    [HttpGet]
    [LimitPerMin(8)]
    [ServiceFilter(typeof(SearchRequestFilter))]
    public async Task<IActionResult> Lookup([FromQuery] List<int>? ingredientIds, CancellationToken ct = default)
    {
        if (ingredientIds == null || ingredientIds.Count == 0)
        {
            return PartialView("_LookupResults", new LookupResultsViewModel());
        }

        if (ingredientIds.Count > 100)
        {
            return BadRequest("Select at most 100 ingredients.");
        }

        var ids = groupService.ExpandCanonicalIds([.. ingredientIds]).Distinct().ToList();
        // Only scalar counts are loaded for ranking; never load all recipe bodies and both collections.
        var candidates = await db.Recipes.AsNoTracking()
            .Where(r => r.ConsumedIngredients.Any(ci => ids.Contains(ci.Id)))
            .Select(r => new
            {
                r.Id, r.Name,
                HasImages = r.Images.Any(),
                Total = r.ConsumedIngredients.Count,
                Matched = r.ConsumedIngredients.Count(ci => ids.Contains(ci.Id)),
                Likes = db.RecipeLikes.Count(l => l.RecipeId == r.Id)
            })
            .ToListAsync(ct);
        var scored = candidates.Select(r => new
        {
            r.Id, r.Name, r.HasImages, r.Likes,
            Pct = (int)Math.Round(100.0 * r.Matched / r.Total)
        }).ToList();
        const int maxPerGroup = 24;
        var exact = scored.Where(r => r.Pct == 100)
            .OrderByDescending(r => r.HasImages).ThenByDescending(r => r.Likes)
            .ThenBy(r => r.Name).ThenBy(r => r.Id).ToList();
        var near = scored.Where(r => r.Pct >= 60 && r.Pct < 100)
            .OrderByDescending(r => r.Pct).ThenByDescending(r => r.HasImages)
            .ThenBy(r => r.Name).ThenBy(r => r.Id).ToList();
        var selected = exact.Take(maxPerGroup).Concat(near.Take(maxPerGroup)).ToList();
        var selectedIds = selected.Select(r => r.Id).ToList();
        var allRecipes = await db.Recipes.AsNoTracking().Where(r => selectedIds.Contains(r.Id))
            .SelectCards().ToListAsync(ct);
        var recipeMap = allRecipes.ToDictionary(r => r.Id);
        var nearIds = near.Take(maxPerGroup).Select(r => r.Id).ToList();
        var missing = await db.Recipes.AsNoTracking().Where(r => nearIds.Contains(r.Id))
            .Select(r => new
            {
                r.Id,
                Names = r.ConsumedIngredients.Where(ci => !ids.Contains(ci.Id)).Select(ci => ci.Name).ToList()
            }).ToDictionaryAsync(r => r.Id, r => string.Join("、", r.Names), ct);
        var (localizedNames, localizedDescs) = await recipeLocalization.LoadLocalizedStringsAsync(allRecipes, ct);
        var exactMatches = exact.Take(maxPerGroup).Where(r => recipeMap.ContainsKey(r.Id))
            .Select(r => recipeMap[r.Id]).ToList();
        var nearMatches = near.Take(maxPerGroup).Where(r => recipeMap.ContainsKey(r.Id)).Select(r => new NearMatchViewModel
        {
            Recipe = recipeMap[r.Id],
            MatchPercentage = r.Pct,
            MissingIngredients = missing.GetValueOrDefault(r.Id, ""),
            LikeCount = r.Likes,
            LocalizedName = localizedNames.GetValueOrDefault(r.Id, r.Name),
            LocalizedDescription = localizedDescs.GetValueOrDefault(r.Id, recipeMap[r.Id].Description)
        }).ToList();

        return PartialView("_LookupResults", new LookupResultsViewModel
        {
            ExactMatches = new RecipeCardsViewModel
            {
                Recipes = exactMatches,
                LikeCounts = selected.ToDictionary(r => r.Id, r => r.Likes),
                LocalizedNames = localizedNames,
                LocalizedDescriptions = localizedDescs
            },
            NearMatches = nearMatches,
            Truncated = exact.Count > maxPerGroup || near.Count > maxPerGroup
        });
    }

}
