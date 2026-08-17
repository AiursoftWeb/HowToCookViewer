using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

/// <summary>
/// Scans the avatar storage directory and deletes any image file that is no longer
/// referenced by any user in the database. This reclaims disk space occupied by
/// avatars that were uploaded but whose associated account was later deleted or
/// whose avatar was subsequently replaced.
/// </summary>
public class OrphanAvatarCleanupJob(
    UserManager<User> userManager,
    FeatureFoldersProvider folders,
    ILogger<OrphanAvatarCleanupJob> logger) : IBackgroundJob
{
    // The job runs every 6 hours. Keeping new files for one full interval plus
    // a buffer prevents an upload from being deleted before the user record is saved.
    private static readonly TimeSpan GracePeriod = TimeSpan.FromHours(7);

    public string Name => "Orphan Avatar Cleanup";

    public string Description =>
        "Scans the avatar storage directory and deletes image files " +
        "that are no longer referenced by any user account, freeing disk space. " +
        "Files newer than 7 hours are always kept to prevent upload races.";

    public async Task ExecuteAsync()
    {
        logger.LogInformation("OrphanAvatarCleanupJob started.");

        // 1. Collect all avatar paths currently referenced by users.
        var referencedPaths = await userManager.Users
            .Select(u => u.AvatarRelativePath)
            .ToHashSetAsync();

        // Always keep the default avatar regardless of user references.
        referencedPaths.Add(User.DefaultAvatarPath);

        logger.LogInformation(
            "OrphanAvatarCleanupJob: {Count} avatar path(s) are referenced in the database.",
            referencedPaths.Count);

        // 2. Scan the workspace for files inside the 'avatar/' subdirectory.
        var workspace = folders.GetWorkspaceFolder();
        var avatarDir = Path.Combine(workspace, "avatar");

        if (!Directory.Exists(avatarDir))
        {
            logger.LogInformation(
                "OrphanAvatarCleanupJob: avatar directory does not exist — nothing to clean.");
            return;
        }

        var allAvatarFiles = Directory
            .EnumerateFiles(avatarDir, "*", SearchOption.AllDirectories)
            .ToList();

        logger.LogInformation(
            "OrphanAvatarCleanupJob: {Count} file(s) found in avatar directory.",
            allAvatarFiles.Count);

        // 3. Delete old files whose workspace-relative path is not in the referenced set.
        var now = DateTime.UtcNow;
        var cutoff = now - GracePeriod;
        var deletedCount = 0;
        foreach (var physicalPath in allAvatarFiles)
        {
            var relativePath = Path
                .GetRelativePath(workspace, physicalPath)
                .Replace('\\', '/');

            if (referencedPaths.Contains(relativePath))
                continue;

            try
            {
                // Upload and profile update are separate requests. A newly uploaded file
                // can therefore be temporarily unreferenced while the profile is saved.
                var lastWriteTime = File.GetLastWriteTimeUtc(physicalPath);
                if (lastWriteTime >= cutoff)
                {
                    logger.LogInformation(
                        "OrphanAvatarCleanupJob: keeping recent unreferenced avatar '{RelativePath}' ({Age:N1}h old).",
                        relativePath, (now - lastWriteTime).TotalHours);
                    continue;
                }

                File.Delete(physicalPath);
                deletedCount++;
                logger.LogInformation(
                    "OrphanAvatarCleanupJob: deleted orphan avatar '{RelativePath}'.",
                    relativePath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "OrphanAvatarCleanupJob: failed to delete '{RelativePath}'.",
                    relativePath);
            }
        }

        logger.LogInformation(
            "OrphanAvatarCleanupJob finished. {Deleted}/{Total} orphan file(s) removed.",
            deletedCount, allAvatarFiles.Count);
    }
}
