using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

/// <summary>
/// Removes LocalizedRecipe rows that are no longer meaningful:
/// (1) rows whose parent Recipe has been soft-deleted,
/// (2) rows for cultures that are no longer in the configured LocalizationLanguages setting.
/// Without this, the table grows unboundedly because recipes change often and
/// cultures may be removed over time.
///
/// A staleness guard (LastLocalizedAt age &gt;= <see cref="StalenessThreshold"/>)
/// prevents a delete-then-localize ping-pong when <see cref="LocalizeRecipesJob"/>
/// is still running with an older view of the configured languages and would
/// otherwise re-create rows that this job just removed.
/// </summary>
public class CleanupLocalizedRecipesJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    ILogger<CleanupLocalizedRecipesJob> logger) : IBackgroundJob
{
    /// <summary>
    /// Only rows whose <see cref="LocalizedRecipe.LastLocalizedAt"/> is older than
    /// this threshold are eligible for cleanup.  Rows created/updated more recently
    /// are left alone so that a concurrently-running <see cref="LocalizeRecipesJob"/>
    /// (which may still hold a stale view of the configured languages) can finish
    /// its current batch without being undone.
    /// </summary>
    internal static readonly TimeSpan StalenessThreshold = TimeSpan.FromMinutes(10);

    public string Name => "Cleanup Localized Recipes";

    public string Description =>
        "Removes LocalizedRecipe rows that are orphaned (parent Recipe soft-deleted) " +
        "or belong to cultures no longer in the configured localization languages list.";

    public async Task ExecuteAsync()
    {
        logger.LogInformation("CleanupLocalizedRecipesJob started.");

        var languagesRaw = await settingsService.GetSettingValueAsync(SettingsMap.LocalizationLanguages);
        var configuredCultures = languagesRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();

        var staleCutoff = DateTime.UtcNow - StalenessThreshold;
        var totalDeleted = 0;

        // 1. Delete LocalizedRecipes whose parent Recipe is soft-deleted.
        // Materialise the list of deleted Recipe IDs first to avoid MySQL error 1093:
        // "You can't specify target table 'l' for update in FROM clause".
        var deletedRecipeIds = await db.Recipes
            .IgnoreQueryFilters()
            .Where(r => r.IsDeleted)
            .Select(r => r.Id)
            .ToListAsync();

        int orphaned = 0;
        if (deletedRecipeIds.Count > 0)
        {
            orphaned = await db.LocalizedRecipes
                .IgnoreQueryFilters()
                .Where(lr => deletedRecipeIds.Contains(lr.RecipeId) && lr.LastLocalizedAt < staleCutoff)
                .ExecuteDeleteAsync();
        }

        if (orphaned > 0)
        {
            totalDeleted += orphaned;
            logger.LogInformation(
                "CleanupLocalizedRecipesJob: deleted {Count} orphaned row(s) (parent Recipe soft-deleted).",
                orphaned);
        }

        // 2. Delete LocalizedRecipes for cultures no longer in the configured languages list.
        if (configuredCultures.Count > 0)
        {
            var staleCulture = await db.LocalizedRecipes
                .IgnoreQueryFilters()
                .Where(lr => !configuredCultures.Contains(lr.Culture) && lr.LastLocalizedAt < staleCutoff)
                .ExecuteDeleteAsync();

            if (staleCulture > 0)
            {
                totalDeleted += staleCulture;
                logger.LogInformation(
                    "CleanupLocalizedRecipesJob: deleted {Count} row(s) for removed cultures.",
                    staleCulture);
            }
        }

        logger.LogInformation(
            "CleanupLocalizedRecipesJob finished. {Total} row(s) deleted.",
            totalDeleted);
    }
}
