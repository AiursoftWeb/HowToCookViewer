using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models.RecipesViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Markdig;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Aiursoft.HowToCookViewer.Controllers;

[LimitPerMin]
public class RecipesController(
    TemplateDbContext db,
    UserManager<User> userManager,
    StorageService storageService,
    GlobalSettingsService globalSettingsService,
    RecipeLocalizationService recipeLocalization,
    IStringLocalizer<RecipesController> localizer) : Controller
{
    internal static readonly Dictionary<string, string> CategoryLocalizerKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["vegetable_dish"] = "Vegetable Dishes",
            ["meat_dish"]      = "Meat Dishes",
            ["aquatic"]        = "Aquatic",
            ["breakfast"]      = "Breakfast",
            ["staple"]         = "Staple Food",
            ["soup"]           = "Soups",
            ["drink"]          = "Drinks",
            ["dessert"]        = "Desserts",
            ["condiment"]      = "Condiments",
            ["semi-finished"]  = "Semi-finished",
            ["template"]       = "Templates",
        };

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

        return sortBy switch
        {
            "likes_desc"     => query.OrderByDescending(r => db.RecipeLikes.Count(l => l.RecipeId == r.Id)).ThenBy(r => r.Name),
            "likes_asc"      => query.OrderBy(r => db.RecipeLikes.Count(l => l.RecipeId == r.Id)).ThenBy(r => r.Name),
            "comments_desc"  => query.OrderByDescending(r => db.RecipeComments.Count(c => c.RecipeId == r.Id)).ThenBy(r => r.Name),
            "comments_asc"   => query.OrderBy(r => db.RecipeComments.Count(c => c.RecipeId == r.Id)).ThenBy(r => r.Name),
            "favorites_desc" => query.OrderByDescending(r => db.RecipeFavorites.Count(f => f.RecipeId == r.Id)).ThenBy(r => r.Name),
            "favorites_asc"  => query.OrderBy(r => db.RecipeFavorites.Count(f => f.RecipeId == r.Id)).ThenBy(r => r.Name),
            _                => query.OrderBy(r => r.Name)
        };
    }

    private string GetDisplayName(string? category, int? difficulty, string? sortBy) =>
        sortBy switch
        {
            "likes_desc"     => localizer["Most Liked"].Value,
            "likes_asc"      => localizer["Least Liked"].Value,
            "comments_desc"  => localizer["Most Commented"].Value,
            "comments_asc"   => localizer["Least Commented"].Value,
            "favorites_desc" => localizer["Most Favorited"].Value,
            "favorites_asc"  => localizer["Least Favorited"].Value,
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
            .Build());

        var repoUrl = await globalSettingsService.GetSettingValueAsync(SettingsMap.HowToCookRepoUrl);
        // Convert clone URL to web URL: strip .git suffix, then build edit link
        var repoWebUrl = repoUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? repoUrl[..^4]
            : repoUrl;
        var gitHubEditUrl = $"{repoWebUrl}/edit/master/{recipe.FilePath.Replace('\\', '/')}";

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
            CategoryDisplayName = GetDisplayName(recipe.Category, null, null),
            LocalizedRecipe = localized
        });
    }

    /// <summary>
    /// Re-inserts cover image HTML at the top and concatenates all markdown sections.
    /// If a <paramref name="localized"/> version is provided, its translated text is used instead of the original.
    /// Images in the stored markdown were already stripped; we add them back via StorageService URLs.
    /// </summary>
    private string BuildFullMarkdown(Recipe recipe, LocalizedRecipe? localized = null)
    {
        var name        = (localized?.LocalizedName        is { Length: > 0 } n ? n : null) ?? recipe.Name;
        var description = (localized?.LocalizedDescription is { Length: > 0 } d ? d : null) ?? recipe.Description;
        var ingredients = (localized?.LocalizedIngredients is { Length: > 0 } i ? i : null) ?? recipe.Ingredients;
        var calculation = (localized?.LocalizedCalculation is { Length: > 0 } c ? c : null) ?? recipe.Calculation;
        var steps       = (localized?.LocalizedSteps       is { Length: > 0 } s ? s : null) ?? recipe.Steps;
        var notes       = (localized?.LocalizedNotes       is { Length: > 0 } no ? no : null) ?? recipe.Notes;

        // Localized section headings: use translated terms when locale is not Chinese
        var isLocalized = localized != null;
        var (hIngredients, hCalculation, hSteps, hNotes) = isLocalized
            ? ("## Ingredients and Tools", "## Calculation", "## Steps", "## Additional Notes")
            : ("## 必备原料和工具", "## 计算", "## 操作", "## 附加内容");

        var parts = new List<string> { $"# {name}" };

        // Cover image block (displayed at top for detail page)
        var cover = recipe.Images.FirstOrDefault(i => i.IsCover);
        if (cover != null)
        {
            var url = storageService.RelativePathToInternetUrl(cover.LogicalPath);
            parts.Add($"![]({url})");
        }

        if (!string.IsNullOrWhiteSpace(description))
            parts.Add(description);

        if (!string.IsNullOrWhiteSpace(ingredients))
            parts.Add($"{hIngredients}\n{ingredients}");

        if (!string.IsNullOrWhiteSpace(calculation))
            parts.Add($"{hCalculation}\n{calculation}");

        if (!string.IsNullOrWhiteSpace(steps))
            parts.Add($"{hSteps}\n{steps}");

        if (!string.IsNullOrWhiteSpace(notes))
            parts.Add($"{hNotes}\n{notes}");

        // Inline all extra images as an image gallery
        var extras = recipe.Images.Where(i => !i.IsCover).ToList();
        if (extras.Count > 0)
        {
            parts.Add("## 图片");
            foreach (var img in extras)
            {
                var url = storageService.RelativePathToInternetUrl(img.LogicalPath);
                parts.Add($"![]({url})");
            }
        }

        return string.Join("\n\n", parts);
    }
}
