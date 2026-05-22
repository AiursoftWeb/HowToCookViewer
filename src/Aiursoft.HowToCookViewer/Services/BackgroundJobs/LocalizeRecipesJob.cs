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
        try
        {
            logger.LogInformation(
                "LocalizeRecipesJob: translating recipe '{Name}' (id={Id}) to {Culture}.",
                recipe.Name, recipe.Id, culture);

            // Parallelize translation calls to reduce overhead
            var nameTask = translator.TranslateAsync(recipe.Name, culture);
            var descTask = translator.TranslateAsync(recipe.Description, culture);
            var ingrTask = translator.TranslateAsync(recipe.Ingredients, culture);
            var calcTask = translator.TranslateAsync(recipe.Calculation, culture);
            var stepTask = translator.TranslateAsync(recipe.Steps, culture);
            var noteTask = translator.TranslateAsync(recipe.Notes, culture);

            await Task.WhenAll(nameTask, descTask, ingrTask, calcTask, stepTask, noteTask);

            var existing = await db.LocalizedRecipes
                .FirstOrDefaultAsync(lr => lr.RecipeId == recipe.Id && lr.Culture == culture);

            if (existing == null)
            {
                db.LocalizedRecipes.Add(new LocalizedRecipe
                {
                    RecipeId = recipe.Id,
                    Culture = culture,
                    LocalizedName = await nameTask,
                    LocalizedDescription = await descTask,
                    LocalizedIngredients = await ingrTask,
                    LocalizedCalculation = await calcTask,
                    LocalizedSteps = await stepTask,
                    LocalizedNotes = await noteTask,
                    LastLocalizedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.LocalizedName = await nameTask;
                existing.LocalizedDescription = await descTask;
                existing.LocalizedIngredients = await ingrTask;
                existing.LocalizedCalculation = await calcTask;
                existing.LocalizedSteps = await stepTask;
                existing.LocalizedNotes = await noteTask;
                existing.LastLocalizedAt = DateTime.UtcNow;
            }
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "LocalizeRecipesJob: failed to localize recipe '{Name}' to {Culture}.",
                recipe.Name, culture);
            return false;
        }
    }
}
