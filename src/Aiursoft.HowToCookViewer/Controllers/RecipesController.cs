using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models.RecipesViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Aiursoft.WebTools.Attributes;
using Markdig;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Localization;

namespace Aiursoft.HowToCookViewer.Controllers;

[LimitPerMin]
public class RecipesController(
    TemplateDbContext db,
    UserManager<User> userManager,
    StorageService storageService,
    GlobalSettingsService globalSettingsService,
    RecipeLocalizationService recipeLocalization,
    RecipeContributorService recipeContributorService,
    RecipeEmbeddingCache embeddingCache,
    RecipeVectorSearchService vectorSearchService,
    IStringLocalizer<RecipesController> localizer) : Controller
{
    internal static readonly Dictionary<string, string> CategoryLocalizerKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["vegetable_dish"] = "Vegetable Dishes",
            ["meat_dish"] = "Meat Dishes",
            ["aquatic"] = "Aquatic",
            ["breakfast"] = "Breakfast",
            ["staple"] = "Staple Food",
            ["soup"] = "Soups",
            ["drink"] = "Drinks",
            ["dessert"] = "Desserts",
            ["condiment"] = "Condiments",
            ["semi-finished"] = "Semi-finished",
            ["template"] = "Templates",
        };

    [ExcludeFromCodeCoverage]
    // ReSharper disable once UnusedMember.Local
    private void _useless_for_localizer()
    {
        _ = localizer["Vegetable Dishes"];
        _ = localizer["Meat Dishes"];
        _ = localizer["Aquatic"];
        _ = localizer["Breakfast"];
        _ = localizer["Staple Food"];
        _ = localizer["Soups"];
        _ = localizer["Drinks"];
        _ = localizer["Desserts"];
        _ = localizer["Condiments"];
        _ = localizer["Semi-finished"];
        _ = localizer["Templates"];

        _ = localizer["Most Liked"];
        _ = localizer["Least Liked"];
        _ = localizer["Most Commented"];
        _ = localizer["Least Commented"];
        _ = localizer["Most Favorited"];
        _ = localizer["Least Favorited"];
        _ = localizer["Difficulty {0} Stars"];
        _ = localizer["All Recipes"];

        _ = localizer["Ingredients and Tools"];
        _ = localizer["Calculation"];
        _ = localizer["Steps"];
        _ = localizer["Additional Notes"];
        _ = localizer["Images"];
        _ = localizer["Estimated Calories"];

        _ = localizer["By Calories"];
        _ = localizer["Highest Calories"];
        _ = localizer["Lowest Calories"];
    }

    public async Task<IActionResult> Index(string? category, int? difficulty, string? sortBy)
    {
        var baseQuery = BuildQuery(category, difficulty, sortBy);
        var totalCount = await baseQuery.CountAsync();
        var recipes = await baseQuery
            .Include(r => r.Images)
            .Take(IndexViewModel.PageSize)
            .ToListAsync();

        var displayName = GetDisplayName(category, difficulty, sortBy);
        var (localizedNames, localizedDescs) = await recipeLocalization.LoadLocalizedStringsAsync(recipes);

        return this.StackView(new IndexViewModel
        {
            PageTitle = displayName,
            Category = category,
            Difficulty = difficulty,
            SortBy = sortBy,
            CategoryDisplayName = displayName,
            Recipes = recipes,
            TotalCount = totalCount,
            HasMore = totalCount > IndexViewModel.PageSize,
            LikeCounts = await LoadLikeCountsAsync(recipes),
            LocalizedNames = localizedNames,
            LocalizedDescriptions = localizedDescs
        });
    }

    [HttpGet]
    public async Task<IActionResult> LoadMore(string? category, int? difficulty, string? sortBy, int page = 2)
    {
        page = Math.Max(2, page);
        var baseQuery = BuildQuery(category, difficulty, sortBy);
        var totalCount = await baseQuery.CountAsync();
        var recipes = await baseQuery
            .Include(r => r.Images)
            .Skip((page - 1) * IndexViewModel.PageSize)
            .Take(IndexViewModel.PageSize)
            .ToListAsync();

        var hasMore = page * IndexViewModel.PageSize < totalCount;
        Response.Headers["X-Has-More"] = hasMore ? "true" : "false";
        Response.Headers["X-Next-Page"] = (page + 1).ToString();

        var (localizedNames, localizedDescs) = await recipeLocalization.LoadLocalizedStringsAsync(recipes);
        return PartialView("_RecipeCards", new RecipeCardsViewModel
        {
            Recipes = recipes,
            LikeCounts = await LoadLikeCountsAsync(recipes),
            LocalizedNames = localizedNames,
            LocalizedDescriptions = localizedDescs
        });
    }

    private IQueryable<Recipe> BuildQuery(string? category, int? difficulty, string? sortBy)
    {
        var query = db.Recipes.AsQueryable();

        if (!string.IsNullOrEmpty(category))
            query = query.Where(r => r.Category == category);

        if (difficulty.HasValue)
            query = query.Where(r => r.Difficulty == difficulty.Value);

        IOrderedQueryable<Recipe> ordered = sortBy switch
        {
            "likes_desc" => query.OrderByDescending(r => db.RecipeLikes.Count(l => l.RecipeId == r.Id)),
            "likes_asc" => query.OrderBy(r => db.RecipeLikes.Count(l => l.RecipeId == r.Id)),
            "comments_desc" => query.OrderByDescending(r => db.RecipeComments.Count(c => c.RecipeId == r.Id)),
            "comments_asc" => query.OrderBy(r => db.RecipeComments.Count(c => c.RecipeId == r.Id)),
            "favorites_desc" => query.OrderByDescending(r => db.RecipeFavorites.Count(f => f.RecipeId == r.Id)),
            "favorites_asc" => query.OrderBy(r => db.RecipeFavorites.Count(f => f.RecipeId == r.Id)),
            "calories_desc" => query.OrderByDescending(r => r.Calories ?? double.MinValue),
            "calories_asc" => query.OrderBy(r => r.Calories ?? double.MaxValue),
            _ => query.OrderByDescending(r => r.Images.Any())
        };

        if (string.IsNullOrEmpty(sortBy))
        {
            return ordered
                .ThenByDescending(r => db.RecipeLikes.Count(l => l.RecipeId == r.Id))
                .ThenByDescending(r => db.RecipeFavorites.Count(f => f.RecipeId == r.Id))
                .ThenBy(r => r.Name);
        }

        return ordered
            .ThenByDescending(r => r.Images.Any())
            .ThenByDescending(r => db.RecipeLikes.Count(l => l.RecipeId == r.Id))
            .ThenByDescending(r => db.RecipeFavorites.Count(f => f.RecipeId == r.Id))
            .ThenBy(r => r.Name);
    }

    private string GetDisplayName(string? category, int? difficulty, string? sortBy) =>
        sortBy switch
        {
            "likes_desc" => localizer["Most Liked"].Value,
            "likes_asc" => localizer["Least Liked"].Value,
            "comments_desc" => localizer["Most Commented"].Value,
            "comments_asc" => localizer["Least Commented"].Value,
            "favorites_desc" => localizer["Most Favorited"].Value,
            "favorites_asc" => localizer["Least Favorited"].Value,
            "calories_desc" => localizer["Highest Calories"].Value,
            "calories_asc" => localizer["Lowest Calories"].Value,
            _ => difficulty.HasValue
                ? localizer["Difficulty {0} Stars", difficulty.Value].Value
                : string.IsNullOrEmpty(category)
                    ? localizer["All Recipes"].Value
                    : localizer[CategoryLocalizerKeys.TryGetValue(category, out var key) ? key : category].Value
        };

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

    public async Task<IActionResult> Random(string? category, int? difficulty, string? sortBy)
    {
        var query = db.Recipes.AsQueryable();

        if (!string.IsNullOrEmpty(category))
            query = query.Where(r => r.Category == category);

        if (difficulty.HasValue)
            query = query.Where(r => r.Difficulty == difficulty.Value);

        var ids = await query.Select(r => r.Id).ToListAsync();
        if (ids.Count == 0)
            return RedirectToAction(nameof(Index), new { category, difficulty, sortBy });

        var randomId = ids[System.Random.Shared.Next(ids.Count)];
        return RedirectToAction(nameof(Detail), new { id = randomId });
    }

    public async Task<IActionResult> Detail(int id)
    {
        var recipe = await db.Recipes
            .Include(r => r.Images)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (recipe == null)
            return NotFound();

        // Detect current request culture and look for a localized version
        var currentCulture = HttpContext.Features.Get<Microsoft.AspNetCore.Localization.IRequestCultureFeature>()
            ?.RequestCulture.Culture.Name ?? string.Empty;
        var localized = await db.LocalizedRecipes
            .FirstOrDefaultAsync(lr => lr.RecipeId == id && lr.Culture == currentCulture);

        var userId = userManager.GetUserId(User);
        var isFavorited = userId != null &&
            await db.RecipeFavorites.AnyAsync(f => f.UserId == userId && f.RecipeId == id);
        var isLiked = userId != null &&
            await db.RecipeLikes.AnyAsync(l => l.UserId == userId && l.RecipeId == id);
        var likeCount = await db.RecipeLikes.CountAsync(l => l.RecipeId == id);

        var comments = await db.RecipeComments
            .Where(c => c.RecipeId == id && c.ParentCommentId == null)
            .Include(c => c.User)
            .Include(c => c.Replies).ThenInclude(r => r.User)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        var markdown = BuildFullMarkdown(recipe, localized);
        var html = Markdown.ToHtml(markdown, new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .UseMermaid()
            .Build());

        var repoUrl = await globalSettingsService.GetSettingValueAsync(SettingsMap.HowToCookRepoUrl);
        // Convert clone URL to web URL: strip .git suffix, then build edit link
        var repoWebUrl = repoUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? repoUrl[..^4]
            : repoUrl;
        var gitHubEditUrl = $"{repoWebUrl}/edit/master/{recipe.FilePath.Replace('\\', '/')}";
        var gitHubHistoryUrl = $"{repoWebUrl}/commits/master/{recipe.FilePath.Replace('\\', '/')}";
        var contributors = await recipeContributorService.GetContributorsAsync(recipe.FilePath);

        return this.StackView(new DetailViewModel
        {
            PageTitle = localized?.LocalizedName ?? recipe.Name,
            Recipe = recipe,
            RenderedMarkdown = html,
            IsFavorited = isFavorited,
            IsLiked = isLiked,
            LikeCount = likeCount,
            Comments = comments,
            GitHubEditUrl = gitHubEditUrl,
            GitHubHistoryUrl = gitHubHistoryUrl,
            CategoryDisplayName = GetDisplayName(recipe.Category, null, null),
            LocalizedRecipe = localized,
            Contributors = contributors,
            ShowSimilarRecipesButton = embeddingCache.Count > 0 && embeddingCache.Count >= await db.Recipes.CountAsync()
        });
    }

    [HttpGet]
    public async Task<IActionResult> Similar(int id)
    {
        var sourceRecipe = await db.Recipes
            .Include(r => r.Images)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (sourceRecipe == null)
            return NotFound();

        var recipes = await vectorSearchService.GetSimilarRecipesAsync(db.Recipes, id, 20);
        var (localizedNames, localizedDescs) = await recipeLocalization.LoadLocalizedStringsAsync(recipes);

        return this.StackView(new SimilarViewModel
        {
            SourceRecipe = sourceRecipe,
            CategoryDisplayName = GetDisplayName(sourceRecipe.Category, null, null),
            SimilarRecipes = recipes,
            LikeCounts = await LoadLikeCountsAsync(recipes),
            LocalizedNames = localizedNames,
            LocalizedDescriptions = localizedDescs
        });
    }

    /// <summary>
    /// Re-inserts cover image HTML at the top and concatenates all markdown sections.
    /// If a <paramref name="localized"/> version is provided, its translated text is used instead of the original.
    /// Images in the stored markdown were already stripped; we add them back via StorageService URLs.
    /// </summary>
    private string BuildFullMarkdown(Recipe recipe, LocalizedRecipe? localized = null)
    {
        var name = (localized?.LocalizedName is { Length: > 0 } n ? n : null) ?? recipe.Name;
        var description = (localized?.LocalizedDescription is { Length: > 0 } d ? d : null) ?? recipe.Description;
        var ingredients = (localized?.LocalizedIngredients is { Length: > 0 } i ? i : null) ?? recipe.Ingredients;
        var calculation = (localized?.LocalizedCalculation is { Length: > 0 } c ? c : null) ?? recipe.Calculation;
        var steps = (localized?.LocalizedSteps is { Length: > 0 } s ? s : null) ?? recipe.Steps;
        var notes = (localized?.LocalizedNotes is { Length: > 0 } no ? no : null) ?? recipe.Notes;

        // Localized section headings
        var hIngredients = $"## {localizer["Ingredients and Tools"].Value}";
        var hCalculation = $"## {localizer["Calculation"].Value}";
        var hSteps = $"## {localizer["Steps"].Value}";
        var hNotes = $"## {localizer["Additional Notes"].Value}";

        var parts = new List<string> { $"# {name}" };

        // Cover image block (displayed at top for detail page)
        var cover = recipe.Images.FirstOrDefault(p => p.IsCover);
        if (cover != null)
        {
            var url = storageService.RelativePathToInternetUrl(cover.LogicalPath);
            parts.Add($"![]({url})");
        }

        if (!string.IsNullOrWhiteSpace(description))
            parts.Add(description);

        if (recipe.Calories.HasValue)
            parts.Add($"**{localizer["Estimated Calories"].Value}：{recipe.Calories.Value} kcal**");

        if (!string.IsNullOrWhiteSpace(ingredients))
            parts.Add($"{hIngredients}\n{ingredients}");

        if (!string.IsNullOrWhiteSpace(calculation))
            parts.Add($"{hCalculation}\n{calculation}");

        if (!string.IsNullOrWhiteSpace(steps))
            parts.Add($"{hSteps}\n{steps}");

        if (!string.IsNullOrWhiteSpace(notes))
            parts.Add($"{hNotes}\n{notes}");

        // Inline all extra images as an image gallery
        var extras = recipe.Images.Where(p => !p.IsCover).ToList();
        if (extras.Count > 0)
        {
            parts.Add($"## {localizer["Images"].Value}");
            foreach (var img in extras)
            {
                var url = storageService.RelativePathToInternetUrl(img.LogicalPath);
                parts.Add($"![]({url})");
            }
        }

        return string.Join("\n\n", parts);
    }
}
