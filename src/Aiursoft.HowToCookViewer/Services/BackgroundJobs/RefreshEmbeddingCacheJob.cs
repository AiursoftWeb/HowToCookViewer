using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

/// <summary>
/// Periodically reloads the in-memory embedding cache from the database.
/// This job is independent from GenerateEmbeddingsJob — it only reads, never calls the model.
/// </summary>
public class RefreshEmbeddingCacheJob(
    TemplateDbContext db,
    RecipeEmbeddingCache cache,
    GlobalSettingsService settingsService,
    ILogger<RefreshEmbeddingCacheJob> logger) : IBackgroundJob
{
    public string Name => "Refresh Embedding Cache";

    public string Description =>
        "Reloads the in-memory recipe embedding cache from the database.";

    public async Task ExecuteAsync()
    {
        var useAiSearch = await settingsService.GetBoolSettingAsync(SettingsMap.EnableEmbeddingBasedSearch);
        if (!useAiSearch)
        {
            logger.LogInformation("RefreshEmbeddingCacheJob: EnableEmbeddingBasedSearch is disabled. Skipping.");
            return;
        }

        await cache.LoadAsync(db);
        logger.LogInformation("RefreshEmbeddingCacheJob: Cache refreshed. {Count} embeddings loaded.", cache.Count);
    }
}
