using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models.FavoritesViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.HowToCookViewer.Controllers;

[Authorize]
[LimitPerMin]
public class FavoritesController(
    TemplateDbContext db,
    UserManager<User> userManager) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Settings",
        NavGroupOrder = 9998,
        CascadedLinksGroupName = "Personal",
        CascadedLinksIcon = "user-circle",
        CascadedLinksOrder = 1,
        LinkText = "My Favorites",
        LinkOrder = 2)]
    [HttpGet]
    [ExcludeFromCodeCoverage]
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User)!;
        var favorites = await db.RecipeFavorites
            .Where(f => f.UserId == userId)
            .Include(f => f.Recipe)
                .ThenInclude(r => r.Images)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync();

        return this.StackView(new IndexViewModel { Favorites = favorites });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int recipeId)
    {
        var userId = userManager.GetUserId(User)!;
        var existing = await db.RecipeFavorites
            .FirstOrDefaultAsync(f => f.UserId == userId && f.RecipeId == recipeId);

        if (existing == null)
            db.RecipeFavorites.Add(new RecipeFavorite { UserId = userId, RecipeId = recipeId });
        else
            db.RecipeFavorites.Remove(existing);

        await db.SaveChangesAsync();
        return RedirectToAction("Detail", "Recipes", new { id = recipeId });
    }
}
