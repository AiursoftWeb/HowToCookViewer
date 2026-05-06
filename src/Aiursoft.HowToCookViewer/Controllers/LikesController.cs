using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Controllers;

[Authorize]
[LimitPerMin]
public class LikesController(
    TemplateDbContext db,
    UserManager<User> userManager) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int recipeId)
    {
        var recipeExists = await db.Recipes.AnyAsync(r => r.Id == recipeId);
        if (!recipeExists)
            return NotFound();

        var userId = userManager.GetUserId(User)!;
        var existing = await db.RecipeLikes
            .FirstOrDefaultAsync(l => l.UserId == userId && l.RecipeId == recipeId);

        if (existing != null)
            db.RecipeLikes.Remove(existing);
        else
            db.RecipeLikes.Add(new RecipeLike { UserId = userId, RecipeId = recipeId });

        await db.SaveChangesAsync();
        return RedirectToAction("Detail", "Recipes", new { id = recipeId });
    }
}
