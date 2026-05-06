using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Controllers;

[Authorize]
[LimitPerMin]
public class CommentsController(
    TemplateDbContext db,
    UserManager<User> userManager) : Controller
{
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Post(int recipeId, int? parentCommentId, string content)
    {
        if (string.IsNullOrWhiteSpace(content) || content.Length > 1000)
            return BadRequest();

        var recipeExists = await db.Recipes.AnyAsync(r => r.Id == recipeId);
        if (!recipeExists)
            return NotFound();

        if (parentCommentId.HasValue)
        {
            var parent = await db.RecipeComments
                .FirstOrDefaultAsync(c => c.Id == parentCommentId.Value && c.RecipeId == recipeId);
            if (parent == null || parent.ParentCommentId != null) // max 2 levels
                return BadRequest();
        }

        var userId = userManager.GetUserId(User)!;
        db.RecipeComments.Add(new RecipeComment
        {
            RecipeId = recipeId,
            UserId = userId,
            ParentCommentId = parentCommentId,
            Content = content.Trim(),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        return RedirectToAction("Detail", "Recipes", new { id = recipeId }, "comments");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int commentId)
    {
        var comment = await db.RecipeComments
            .Include(c => c.Replies)
            .FirstOrDefaultAsync(c => c.Id == commentId);

        if (comment == null)
            return NotFound();

        var userId = userManager.GetUserId(User);
        if (comment.UserId != userId)
            return Forbid();

        // Delete replies first, then the comment itself
        db.RecipeComments.RemoveRange(comment.Replies);
        db.RecipeComments.Remove(comment);
        await db.SaveChangesAsync();

        return RedirectToAction("Detail", "Recipes", new { id = comment.RecipeId }, "comments");
    }
}
