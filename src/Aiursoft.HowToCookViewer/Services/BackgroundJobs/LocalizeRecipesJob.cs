using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

/// <summary>
/// Periodically translates recipe content into the configured languages using an AI endpoint.
/// Each run processes at most <see cref="BatchSize"/> (recipe, culture) pairs to avoid long runs.
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
        var instance = await settingsService.GetSettingValueAsync(SettingsMap.OllamaInstance);
        var model = await settingsService.GetSettingValueAsync(SettingsMap.OllamaModel);
        var token = await settingsService.GetSettingValueAsync(SettingsMap.OllamaToken);
        var languagesRaw = await settingsService.GetSettingValueAsync(SettingsMap.LocalizationLanguages);

        if (string.IsNullOrWhiteSpace(instance) || string.IsNullOrWhiteSpace(model))
        {
            logger.LogInformation("LocalizeRecipesJob: Ollama endpoint or model not configured. Skipping.");
            return;
        }

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
            // Pull pending recipes in pages to avoid loading the entire table into memory
            while (true)
            {
                var pendingRecipes = await db.Recipes
                    .Where(r => !db.LocalizedRecipes.Any(lr =>
                        lr.RecipeId == r.Id &&
                        lr.Culture == culture &&
                        lr.LastLocalizedAt >= r.FileLastModified))
                    .OrderBy(r => r.Id)
                    .Take(20)
                    .ToListAsync();

                if (pendingRecipes.Count == 0) break;

                foreach (var recipe in pendingRecipes)
                {
                    await LocalizeRecipeAsync(recipe, culture, instance, model, token);
                    totalProcessed++;
                }

                // Save after every page so progress survives a restart
                await db.SaveChangesAsync();

                logger.LogInformation(
                    "LocalizeRecipesJob: [{Culture}] saved a batch of {Count} (total so far: {Total}).",
                    culture, pendingRecipes.Count, totalProcessed);
            }

            logger.LogInformation("LocalizeRecipesJob: [{Culture}] all recipes up-to-date.", culture);
        }

        logger.LogInformation("LocalizeRecipesJob: done. Processed {Count} recipe/language pair(s) this run.", totalProcessed);
    }

    private async Task LocalizeRecipeAsync(
        Recipe recipe,
        string culture,
        string instance,
        string model,
        string token)
    {
        try
        {
            logger.LogInformation(
                "LocalizeRecipesJob: translating recipe '{Name}' (id={Id}) to {Culture}.",
                recipe.Name, recipe.Id, culture);

            var localizedName        = await translator.TranslateAsync(recipe.Name,        culture, instance, model, token);
            var localizedDescription = await translator.TranslateAsync(recipe.Description, culture, instance, model, token);
            var localizedIngredients = await translator.TranslateAsync(recipe.Ingredients, culture, instance, model, token);
            var localizedCalculation = await translator.TranslateAsync(recipe.Calculation, culture, instance, model, token);
            var localizedSteps       = await translator.TranslateAsync(recipe.Steps,       culture, instance, model, token);
            var localizedNotes       = await translator.TranslateAsync(recipe.Notes,       culture, instance, model, token);

            var existing = await db.LocalizedRecipes
                .FirstOrDefaultAsync(lr => lr.RecipeId == recipe.Id && lr.Culture == culture);

            if (existing == null)
            {
                db.LocalizedRecipes.Add(new LocalizedRecipe
                {
                    RecipeId            = recipe.Id,
                    Culture             = culture,
                    LocalizedName        = localizedName,
                    LocalizedDescription = localizedDescription,
                    LocalizedIngredients = localizedIngredients,
                    LocalizedCalculation = localizedCalculation,
                    LocalizedSteps       = localizedSteps,
                    LocalizedNotes       = localizedNotes,
                    LastLocalizedAt      = DateTime.UtcNow
                });
            }
            else
            {
                existing.LocalizedName        = localizedName;
                existing.LocalizedDescription = localizedDescription;
                existing.LocalizedIngredients = localizedIngredients;
                existing.LocalizedCalculation = localizedCalculation;
                existing.LocalizedSteps       = localizedSteps;
                existing.LocalizedNotes       = localizedNotes;
                existing.LastLocalizedAt      = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "LocalizeRecipesJob: failed to localize recipe '{Name}' to {Culture}.",
                recipe.Name, culture);
        }
    }
}
