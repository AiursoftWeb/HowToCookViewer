using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

public class IndexTipsJob(
    StorageRootPathProvider storageRootPathProvider,
    TemplateDbContext db,
    ILogger<IndexTipsJob> logger) : IBackgroundJob
{
    public string Name => "Index HowToCook Tips";
    public string Description => "Parses all HowToCook tip Markdown files and upserts them into the database.";

    public async Task ExecuteAsync()
    {
        var repoPath = Path.Combine(storageRootPathProvider.GetStorageRootPath(), "repo");
        var tipsPath = Path.Combine(repoPath, "tips");

        if (!Directory.Exists(tipsPath))
        {
            logger.LogWarning("IndexTipsJob: tips directory not found at '{TipsPath}'. Skipping.", tipsPath);
            return;
        }

        var markdownFiles = Directory.GetFiles(tipsPath, "*.md", SearchOption.AllDirectories);
        logger.LogInformation("IndexTipsJob: found {Count} tip markdown files.", markdownFiles.Length);

        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var validFilePaths = new HashSet<string>();

        foreach (var absoluteFilePath in markdownFiles)
        {
            var relativeFilePath = Path.GetRelativePath(repoPath, absoluteFilePath).Replace('\\', '/');
            validFilePaths.Add(relativeFilePath);
            try
            {
                var existing = await db.Tips
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.FilePath == relativeFilePath);

                var content = await File.ReadAllTextAsync(absoluteFilePath);
                var parts = relativeFilePath.Split('/');
                var category = parts.Length >= 3 ? parts[1] : "root";
                var title = Path.GetFileNameWithoutExtension(parts[^1]);
                var translationSourceChanged = existing == null ||
                    !string.Equals(existing.Title, title, StringComparison.Ordinal) ||
                    !string.Equals(existing.Content, content, StringComparison.Ordinal);
                var metadataChanged = existing == null ||
                    !string.Equals(existing.Category, category, StringComparison.Ordinal);

                if (existing != null && !existing.IsDeleted &&
                    !translationSourceChanged && !metadataChanged)
                {
                    skipped++;
                    continue;
                }

                // In a depth-1 clone git attributes HEAD to every path. Preserve the
                // localization revision unless the fields sent to the translator changed.
                var translationLastModified = existing?.FileLastModified ??
                    await GetGitLastModifiedAsync(repoPath, relativeFilePath);
                if (existing != null && translationSourceChanged)
                {
                    translationLastModified = await GetGitLastModifiedAsync(repoPath, relativeFilePath);
                }

                if (existing == null)
                {
                    db.Tips.Add(new Tip
                    {
                        Title = title,
                        Category = category,
                        FilePath = relativeFilePath,
                        Content = content,
                        FileLastModified = translationLastModified
                    });
                    inserted++;
                }
                else
                {
                    var tip = await db.Tips
                        .IgnoreQueryFilters()
                        .FirstAsync(t => t.FilePath == relativeFilePath);
                    tip.Title = title;
                    tip.Category = category;
                    tip.Content = content;
                    tip.FileLastModified = translationLastModified;
                    tip.IsDeleted = false;
                    updated++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "IndexTipsJob: failed to process '{File}'.", relativeFilePath);
            }
        }

        var allDbTips = await db.Tips
            .IgnoreQueryFilters()
            .Where(t => !t.IsDeleted)
            .ToListAsync();

        var deletedCount = 0;
        foreach (var t in allDbTips)
        {
            if (!validFilePaths.Contains(t.FilePath))
            {
                t.IsDeleted = true;
                deletedCount++;
            }
        }

        await db.SaveChangesAsync();
        logger.LogInformation("IndexTipsJob complete: {Inserted} inserted, {Updated} updated, {Skipped} skipped, {Deleted} deleted.", inserted, updated, skipped, deletedCount);
    }

    private async Task<DateTime> GetGitLastModifiedAsync(string repoPath, string relativeFilePath)
    {
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"log -1 --format=%cI -- \"{relativeFilePath}\"",
                WorkingDirectory = repoPath,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (DateTimeOffset.TryParse(output.Trim(), out var dt)) return dt.UtcDateTime;
        logger.LogWarning("IndexTipsJob: could not parse git log date for '{File}'. Using UtcNow.", relativeFilePath);
        return DateTime.UtcNow;
    }
}
