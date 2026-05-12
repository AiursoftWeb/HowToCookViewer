using Aiursoft.HowToCookViewer.Entities;
using IngredientIndexVm = Aiursoft.HowToCookViewer.Models.IngredientsViewModels.IndexViewModel;
using Aiursoft.HowToCookViewer.Models.IngredientsViewModels;
using Aiursoft.HowToCookViewer.Models.RecipesViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.UiStack.Navigation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Localization;

namespace Aiursoft.HowToCookViewer.Controllers;

public class IngredientsController(
    TemplateDbContext db,
    RecipeLocalizationService recipeLocalization,
    IStringLocalizer<IngredientsController> localizer) : Controller
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
        var ingredients = await db.Ingredients
            .AsNoTracking()
            .OrderByDescending(i => i.Recipes.Count)
            .ThenBy(i => i.Name)
            .ToListAsync();

        var preSelectedIds = ingredients
            .Take(20)
            .Select(i => i.Id)
            .ToHashSet();

        return this.StackView(new IngredientIndexVm
        {
            AllIngredients = ingredients,
            IngredientCount = ingredients.Count,
            PreSelectedIds = preSelectedIds
        });
    }

    [HttpGet]
    public async Task<IActionResult> Lookup([FromQuery] List<int>? ingredientIds)
    {
        if (ingredientIds == null || ingredientIds.Count == 0)
        {
            return PartialView("_LookupResults", new LookupResultsViewModel());
        }

        var ids = ingredientIds.Distinct().ToList();

        // Load all candidates that have at least one matching ingredient
        var candidates = await db.Recipes
            .AsNoTracking()
            .Include(r => r.Images)
            .Include(r => r.ConsumedIngredients)
            .Where(r => r.ConsumedIngredients.Any(ci => ids.Contains(ci.Id)))
            .ToListAsync();

        // Split into exact matches (100%) and near matches (>=60%, <100%)
        var exactMatches = new List<Recipe>();
        var nearMatchData = new List<(Recipe Recipe, int Pct, string Missing)>();

        foreach (var recipe in candidates)
        {
            var total = recipe.ConsumedIngredients.Count;
            var matched = recipe.ConsumedIngredients.Count(ci => ids.Contains(ci.Id));
            var pct = (int)Math.Round(100.0 * matched / total);

            if (pct == 100)
                exactMatches.Add(recipe);
            else if (pct >= 60)
            {
                var missing = string.Join("、",
                    recipe.ConsumedIngredients.Where(ci => !ids.Contains(ci.Id)).Select(ci => ci.Name));
                nearMatchData.Add((recipe, pct, missing));
            }
        }

        // Order exact matches
        exactMatches = exactMatches
            .OrderByDescending(r => r.Images.Any())
            .ThenByDescending(r => db.RecipeLikes.Count(l => l.RecipeId == r.Id))
            .ThenBy(r => r.Name)
            .ToList();

        // Order near matches by match percentage descending
        nearMatchData = nearMatchData
            .OrderByDescending(n => n.Pct)
            .ThenByDescending(n => n.Recipe.Images.Any())
            .ThenBy(n => n.Recipe.Name)
            .ToList();

        // Load like counts and localization for all recipes involved
        var allRecipes = exactMatches.Concat(nearMatchData.Select(n => n.Recipe)).ToList();
        var likeCounts = await LoadLikeCountsAsync(allRecipes);
        var (localizedNames, localizedDescs) = await recipeLocalization.LoadLocalizedStringsAsync(allRecipes);

        var exactLikeCounts = new Dictionary<int, int>();
        foreach (var r in exactMatches)
            exactLikeCounts[r.Id] = likeCounts.GetValueOrDefault(r.Id);

        var exactNames = new Dictionary<int, string>();
        var exactDescs = new Dictionary<int, string>();
        foreach (var r in exactMatches)
        {
            if (localizedNames.TryGetValue(r.Id, out var n)) exactNames[r.Id] = n;
            if (localizedDescs.TryGetValue(r.Id, out var d)) exactDescs[r.Id] = d;
        }

        var nearMatches = nearMatchData.Select(n => new NearMatchViewModel
        {
            Recipe = n.Recipe,
            MatchPercentage = n.Pct,
            MissingIngredients = n.Missing,
            LikeCount = likeCounts.GetValueOrDefault(n.Recipe.Id),
            LocalizedName = localizedNames.GetValueOrDefault(n.Recipe.Id, n.Recipe.Name),
            LocalizedDescription = localizedDescs.GetValueOrDefault(n.Recipe.Id, n.Recipe.Description)
        }).ToList();

        return PartialView("_LookupResults", new LookupResultsViewModel
        {
            ExactMatches = new RecipeCardsViewModel
            {
                Recipes = exactMatches,
                LikeCounts = exactLikeCounts,
                LocalizedNames = exactNames,
                LocalizedDescriptions = exactDescs
            },
            NearMatches = nearMatches
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
