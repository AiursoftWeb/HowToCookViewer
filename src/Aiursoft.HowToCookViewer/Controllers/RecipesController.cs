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
    IStringLocalizer<RecipesController> localizer) : Controller
{
    private static readonly Dictionary<string, string> CategoryLocalizerKeys =
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
        var query = db.Recipes
            .Include(r => r.Images)
            .AsQueryable();

        if (!string.IsNullOrEmpty(category))
            query = query.Where(r => r.Category == category);

        if (difficulty.HasValue)
            query = query.Where(r => r.Difficulty == difficulty.Value);

        IQueryable<Recipe> ordered = sortBy switch
        {
            "likes_desc"     => query.OrderByDescending(r => db.RecipeLikes.Count(l => l.RecipeId == r.Id)).ThenBy(r => r.Name),
            "likes_asc"      => query.OrderBy(r => db.RecipeLikes.Count(l => l.RecipeId == r.Id)).ThenBy(r => r.Name),
            "comments_desc"  => query.OrderByDescending(r => db.RecipeComments.Count(c => c.RecipeId == r.Id)).ThenBy(r => r.Name),
            "comments_asc"   => query.OrderBy(r => db.RecipeComments.Count(c => c.RecipeId == r.Id)).ThenBy(r => r.Name),
            "favorites_desc" => query.OrderByDescending(r => db.RecipeFavorites.Count(f => f.RecipeId == r.Id)).ThenBy(r => r.Name),
            "favorites_asc"  => query.OrderBy(r => db.RecipeFavorites.Count(f => f.RecipeId == r.Id)).ThenBy(r => r.Name),
            _                => query.OrderBy(r => r.Name)
        };

        var recipes = await ordered.ToListAsync();

        var recipeIds = recipes.Select(r => r.Id).ToList();
        var likeCounts = await db.RecipeLikes
            .Where(l => recipeIds.Contains(l.RecipeId))
            .GroupBy(l => l.RecipeId)
            .Select(g => new { RecipeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RecipeId, x => x.Count);

        var displayName = sortBy switch
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

        return this.StackView(new IndexViewModel
        {
            PageTitle = displayName,
            Category = category,
            Difficulty = difficulty,
            SortBy = sortBy,
            CategoryDisplayName = displayName,
            Recipes = recipes,
            TotalCount = recipes.Count,
            LikeCounts = likeCounts
        });
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

        var markdown = BuildFullMarkdown(recipe);
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
            PageTitle = recipe.Name,
            Recipe = recipe,
            RenderedMarkdown = html,
            IsFavorited = isFavorited,
            IsLiked = isLiked,
            LikeCount = likeCount,
            Comments = comments,
            GitHubEditUrl = gitHubEditUrl
        });
    }

    /// <summary>
    /// Re-inserts cover image HTML at the top and concatenates all markdown sections.
    /// Images in the stored markdown were already stripped; we add them back via StorageService URLs.
    /// </summary>
    private string BuildFullMarkdown(Recipe recipe)
    {
        var parts = new List<string> { $"# {recipe.Name}" };

        // Cover image block (displayed at top for detail page)
        var cover = recipe.Images.FirstOrDefault(i => i.IsCover);
        if (cover != null)
        {
            var url = storageService.RelativePathToInternetUrl(cover.LogicalPath);
            parts.Add($"![]({url})");
        }

        if (!string.IsNullOrWhiteSpace(recipe.Description))
            parts.Add(recipe.Description);

        if (!string.IsNullOrWhiteSpace(recipe.Ingredients))
            parts.Add("## 必备原料和工具\n" + recipe.Ingredients);

        if (!string.IsNullOrWhiteSpace(recipe.Calculation))
            parts.Add("## 计算\n" + recipe.Calculation);

        if (!string.IsNullOrWhiteSpace(recipe.Steps))
            parts.Add("## 操作\n" + recipe.Steps);

        if (!string.IsNullOrWhiteSpace(recipe.Notes))
            parts.Add("## 附加内容\n" + recipe.Notes);

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
