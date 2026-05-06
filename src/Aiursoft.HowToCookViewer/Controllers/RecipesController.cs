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

namespace Aiursoft.HowToCookViewer.Controllers;

[LimitPerMin]
public class RecipesController(
    TemplateDbContext db,
    UserManager<User> userManager,
    StorageService storageService) : Controller
{
    private static readonly Dictionary<string, string> CategoryDisplayNames =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["vegetable_dish"] = "素菜",
            ["meat_dish"]      = "荤菜",
            ["aquatic"]        = "水产",
            ["breakfast"]      = "早餐",
            ["staple"]         = "主食",
            ["soup"]           = "汤品",
            ["drink"]          = "饮料",
            ["dessert"]        = "甜品",
            ["condiment"]      = "调料",
            ["semi-finished"]  = "半成品",
            ["template"]       = "模板",
        };

    public async Task<IActionResult> Index(string? category, int? difficulty)
    {
        var query = db.Recipes
            .Include(r => r.Images)
            .AsQueryable();

        if (!string.IsNullOrEmpty(category))
            query = query.Where(r => r.Category == category);

        if (difficulty.HasValue)
            query = query.Where(r => r.Difficulty == difficulty.Value);

        var recipes = await query
            .OrderBy(r => r.Name)
            .ToListAsync();

        var displayName = difficulty.HasValue
            ? new string('★', difficulty.Value)
            : string.IsNullOrEmpty(category)
                ? "全部菜谱"
                : CategoryDisplayNames.TryGetValue(category, out var name) ? name : category;

        return this.StackView(new IndexViewModel
        {
            PageTitle = displayName,
            Category = category,
            CategoryDisplayName = displayName,
            Recipes = recipes,
            TotalCount = recipes.Count
        });
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

        return this.StackView(new DetailViewModel
        {
            PageTitle = recipe.Name,
            Recipe = recipe,
            RenderedMarkdown = html,
            IsFavorited = isFavorited,
            IsLiked = isLiked,
            LikeCount = likeCount,
            Comments = comments
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
