using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

public class LocalizeTipsJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    RecipeTranslationService translator,
    ILogger<LocalizeTipsJob> logger) : IBackgroundJob
{
    public string Name => "Localize Tips";
    public string Description => "Translates tip content into configured languages using an AI endpoint.";

    public async Task ExecuteAsync()
    {
        if (!await settingsService.IsAiLocalizationEnabledAsync())
        {
            logger.LogInformation("LocalizeTipsJob: Ollama endpoint not configured. Skipping.");
            return;
        }

        var languagesRaw = await settingsService.GetSettingValueAsync(SettingsMap.LocalizationLanguages);

        var cultures = languagesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (cultures.Length == 0)
        {
            logger.LogInformation("LocalizeTipsJob: No target languages configured. Skipping.");
            return;
        }

        logger.LogInformation("LocalizeTipsJob: starting with {Count} target languages: {Languages}", cultures.Length, string.Join(", ", cultures));
        var totalProcessed = 0;

        foreach (var culture in cultures)
        {
            while (true)
            {
                var pendingTips = await db.Tips
                    .Where(t => !db.LocalizedTips.Any(lt =>
                        lt.TipId == t.Id &&
                        lt.Culture == culture &&
                        lt.LastLocalizedAt >= t.FileLastModified))
                    .OrderBy(t => t.Id)
                    .Take(20)
                    .ToListAsync();

                if (pendingTips.Count == 0) break;

                foreach (var tip in pendingTips)
                {
                    await LocalizeTipAsync(tip, culture);
                    totalProcessed++;
                }

                await db.SaveChangesAsync();
                logger.LogInformation("LocalizeTipsJob: [{Culture}] saved a batch of {Count} (total so far: {Total}).", culture, pendingTips.Count, totalProcessed);
            }

            logger.LogInformation("LocalizeTipsJob: [{Culture}] all tips up-to-date.", culture);
        }

        logger.LogInformation("LocalizeTipsJob: done. Processed {Count} tip/language pair(s) this run.", totalProcessed);
    }

    private async Task LocalizeTipAsync(Tip tip, string culture)
    {
        try
        {
            logger.LogInformation("LocalizeTipsJob: translating tip '{Title}' (id={Id}) to {Culture}.", tip.Title, tip.Id, culture);
            var localizedTitle = await translator.TranslateAsync(tip.Title, culture);
            var localizedContent = await translator.TranslateAsync(tip.Content, culture);

            var existing = await db.LocalizedTips.FirstOrDefaultAsync(lt => lt.TipId == tip.Id && lt.Culture == culture);
            if (existing == null)
            {
                db.LocalizedTips.Add(new LocalizedTip
                {
                    TipId = tip.Id,
                    Culture = culture,
                    LocalizedTitle = localizedTitle,
                    LocalizedContent = localizedContent,
                    LastLocalizedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.LocalizedTitle = localizedTitle;
                existing.LocalizedContent = localizedContent;
                existing.LastLocalizedAt = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "LocalizeTipsJob: failed to localize tip '{Title}' to {Culture}.", tip.Title, culture);
        }
    }
}
