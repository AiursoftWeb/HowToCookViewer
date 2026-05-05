using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

/// <summary>
/// Drops all indexed recipe data (DB rows + Workspace images).
/// Never scheduled — trigger manually from the Jobs admin page for testing.
/// </summary>
public class ResetRecipeDataJob(
    TemplateDbContext db,
    FeatureFoldersProvider featureFoldersProvider,
    ILogger<ResetRecipeDataJob> logger) : IBackgroundJob
{
    public string Name => "Reset Recipe Data";

    public string Description =>
        "Deletes all Recipe and RecipeImage rows from the database and removes every file " +
        "under Workspace/recipe-images/. Never runs automatically.";

    public async Task ExecuteAsync()
    {
        // 1. Remove all RecipeImage rows first (FK child)
        var imageCount = await db.RecipeImages.CountAsync();
        db.RecipeImages.RemoveRange(db.RecipeImages);

        // 2. Remove all Recipe rows
        var recipeCount = await db.Recipes.CountAsync();
        db.Recipes.RemoveRange(db.Recipes);

        await db.SaveChangesAsync();

        logger.LogInformation(
            "ResetRecipeDataJob: deleted {RecipeCount} recipes and {ImageCount} image records from the database.",
            recipeCount, imageCount);

        // 3. Wipe Workspace/recipe-images/ directory
        var recipeImagesDir = Path.Combine(featureFoldersProvider.GetWorkspaceFolder(), "recipe-images");
        if (Directory.Exists(recipeImagesDir))
        {
            Directory.Delete(recipeImagesDir, recursive: true);
            logger.LogInformation(
                "ResetRecipeDataJob: deleted directory '{Dir}'.", recipeImagesDir);
        }
        else
        {
            logger.LogInformation(
                "ResetRecipeDataJob: directory '{Dir}' did not exist, nothing to delete.", recipeImagesDir);
        }
    }
}
