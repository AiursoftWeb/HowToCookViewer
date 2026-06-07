using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

/// <summary>
/// Periodically translates recipe content into the configured languages using an AI endpoint.
/// Runs until all pending (recipe, culture) pairs are translated, saving progress along the way.
/// Skips recipes whose <see cref="LocalizedRecipe.LastLocalizedAt"/> is already up-to-date.
/// </summary>
public class LocalizeRecipesJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    RecipeTranslationService translator,
    ILogger<LocalizeRecipesJob> logger) : IBackgroundJob
{
    public string Name => "Localize Recipes";

    public string Description =>
        "Translates recipe content into configured languages using an AI endpoint (Ollama / OpenAI-compatible).";

    public async Task ExecuteAsync()
    {
        if (!await settingsService.IsAiLocalizationEnabledAsync())
        {
            logger.LogInformation("LocalizeRecipesJob: Ollama endpoint not configured. Skipping.");
            return;
        }

        var languagesRaw = await settingsService.GetSettingValueAsync(SettingsMap.LocalizationLanguages);

        var cultures = languagesRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (cultures.Length == 0)
        {
            logger.LogInformation("LocalizeRecipesJob: No target languages configured. Skipping.");
            return;
        }

        logger.LogInformation("LocalizeRecipesJob: starting with {Count} target languages: {Languages}",
            cultures.Length, string.Join(", ", cultures));

        var totalProcessed = 0;

        foreach (var culture in cultures)
        {
            var lastId = 0;
            while (true)
            {
                var currentLastId = lastId;
                var pendingRecipes = await db.Recipes
                    .Where(r => r.Id > currentLastId && !db.LocalizedRecipes.Any(lr =>
                        lr.RecipeId == r.Id &&
                        lr.Culture == culture &&
                        lr.LastLocalizedAt >= r.FileLastModified))
                    .OrderBy(r => r.Id)
                    .Take(20)
                    .ToListAsync();

                if (pendingRecipes.Count == 0) break;

                foreach (var recipe in pendingRecipes)
                {
                    var success = await LocalizeRecipeAsync(recipe, culture);
                    if (success)
                    {
                        totalProcessed++;
                        // Save immediately after each recipe to ensure progress survives a crash
                        await db.SaveChangesAsync();
                    }
                }

                lastId = pendingRecipes.Max(r => r.Id);
                logger.LogInformation(
                    "LocalizeRecipesJob: [{Culture}] batch finished. Last ID: {LastId}. Total processed so far: {Total}.",
                    culture, lastId, totalProcessed);
            }

            logger.LogInformation("LocalizeRecipesJob: [{Culture}] all recipes up-to-date.", culture);
        }

        logger.LogInformation("LocalizeRecipesJob: done. Processed {Count} recipe/language pair(s) this run.", totalProcessed);
    }

    private async Task<bool> LocalizeRecipeAsync(Recipe recipe, string culture)
    {
        // Ensure a row exists so partial progress is never lost.
        var row = await db.LocalizedRecipes
            .FirstOrDefaultAsync(lr => lr.RecipeId == recipe.Id && lr.Culture == culture);

        if (row == null)
        {
            row = new LocalizedRecipe
            {
                RecipeId = recipe.Id,
                Culture = culture,
                LastLocalizedAt = DateTime.MinValue // not yet complete
            };
            db.LocalizedRecipes.Add(row);
            await db.SaveChangesAsync();
        }

        logger.LogInformation(
            "LocalizeRecipesJob: translating recipe '{Name}' (id={Id}) to {Culture}.",
            recipe.Name, recipe.Id, culture);

        // Translate each field sequentially — save after each success.
        if (string.IsNullOrWhiteSpace(row.LocalizedName))
            await TranslateAndSaveAsync(recipe.Name,       v => row.LocalizedName = v, culture);
        if (string.IsNullOrWhiteSpace(row.LocalizedDescription))
            await TranslateAndSaveAsync(recipe.Description, v => row.LocalizedDescription = v, culture);
        if (string.IsNullOrWhiteSpace(row.LocalizedIngredients))
            await TranslateAndSaveAsync(recipe.Ingredients, v => row.LocalizedIngredients = v, culture);
        if (string.IsNullOrWhiteSpace(row.LocalizedCalculation))
            await TranslateAndSaveAsync(recipe.Calculation, v => row.LocalizedCalculation = v, culture);
        if (string.IsNullOrWhiteSpace(row.LocalizedSteps))
            await TranslateAndSaveAsync(recipe.Steps,       v => row.LocalizedSteps = v, culture);
        if (string.IsNullOrWhiteSpace(row.LocalizedNotes))
            await TranslateAndSaveAsync(recipe.Notes,       v => row.LocalizedNotes = v, culture);

        // Mark complete only when every field has content.
        if (!string.IsNullOrWhiteSpace(row.LocalizedName) &&
            !string.IsNullOrWhiteSpace(row.LocalizedDescription) &&
            !string.IsNullOrWhiteSpace(row.LocalizedIngredients) &&
            !string.IsNullOrWhiteSpace(row.LocalizedCalculation) &&
            !string.IsNullOrWhiteSpace(row.LocalizedSteps) &&
            !string.IsNullOrWhiteSpace(row.LocalizedNotes))
        {
            row.LastLocalizedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return true;
    }

    private async Task TranslateAndSaveAsync(string source, Action<string> setter, string culture)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        try
        {
            var translated = await translator.TranslateAsync(source, culture);
            if (!string.IsNullOrWhiteSpace(translated))
            {
                setter(translated);
                await db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LocalizeRecipesJob: translation failed, will retry next run.");
        }
    }
}
