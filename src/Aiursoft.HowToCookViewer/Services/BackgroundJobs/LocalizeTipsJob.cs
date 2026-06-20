using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

public class LocalizeTipsJob(
    TemplateDbContext db,
    GlobalSettingsService settingsService,
    IRecipeTranslationService translator,
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
        // Ensure a row exists so partial progress is never lost.
        var row = await db.LocalizedTips
            .FirstOrDefaultAsync(lt => lt.TipId == tip.Id && lt.Culture == culture);

        if (row == null)
        {
            row = new LocalizedTip
            {
                TipId = tip.Id,
                Culture = culture,
                LastLocalizedAt = DateTime.MinValue // not yet complete
            };
            db.LocalizedTips.Add(row);
            await db.SaveChangesAsync();
        }

        // If the source tip has been updated since the last localization,
        // clear all fields so they will be re-translated.
        if (row.LastLocalizedAt < tip.FileLastModified)
        {
            row.LocalizedTitle = string.Empty;
            row.LocalizedContent = string.Empty;
        }

        logger.LogInformation("LocalizeTipsJob: translating tip '{Title}' (id={Id}) to {Culture}.", tip.Title, tip.Id, culture);

        // Translate each field sequentially — only fill what is still empty.
        if (string.IsNullOrWhiteSpace(row.LocalizedTitle))
            await TranslateTipFieldAsync(tip.Title,   v => row.LocalizedTitle = v, culture);
        if (string.IsNullOrWhiteSpace(row.LocalizedContent))
            await TranslateTipFieldAsync(tip.Content, v => row.LocalizedContent = v, culture);

        // Mark complete only when both fields have content.
        if (!string.IsNullOrWhiteSpace(row.LocalizedTitle) &&
            !string.IsNullOrWhiteSpace(row.LocalizedContent))
        {
            row.LastLocalizedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    private async Task TranslateTipFieldAsync(string source, Action<string> setter, string culture)
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
            logger.LogWarning(ex, "LocalizeTipsJob: translation failed, will retry next run.");
        }
    }
}
