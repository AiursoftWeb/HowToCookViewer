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
/// </summary>
public class CleanupLocalizedRecipesJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    ILogger<CleanupLocalizedRecipesJob> logger) : IBackgroundJob
{
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

        var totalDeleted = 0;

        // 1. Delete LocalizedRecipes whose parent Recipe is soft-deleted.
        var orphaned = await db.LocalizedRecipes
            .IgnoreQueryFilters()
            .Where(lr => lr.Recipe.IsDeleted)
            .ExecuteDeleteAsync();

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
                .Where(lr => !configuredCultures.Contains(lr.Culture))
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
