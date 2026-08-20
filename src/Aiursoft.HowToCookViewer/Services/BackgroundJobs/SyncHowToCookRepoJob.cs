using Aiursoft.Canon.BackgroundJobs;
using Aiursoft.CSTools.Tools;
using Aiursoft.GitRunner;
using Aiursoft.GitRunner.Models;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Services.FileStorage;

namespace Aiursoft.HowToCookViewer.Services.BackgroundJobs;

/// <summary>
/// Periodically resets the local HowToCook git repository to match the latest state
/// of the upstream remote, ensuring the viewer always serves up-to-date content.
/// </summary>
public class SyncHowToCookRepoJob(
    StorageRootPathProvider storageRootPathProvider,
    GlobalSettingsService globalSettingsService,
    WorkspaceManager workspaceManager,
    ILogger<SyncHowToCookRepoJob> logger) : IBackgroundJob
{
    private static readonly SemaphoreSlim SyncSemaphore = new(1, 1);

    public string Name => "Sync HowToCook Repo";

    public string Description =>
        "Atomically replaces the local HowToCook repository with a shallow clone of the latest upstream state.";

    public async Task ExecuteAsync()
    {
        var repoPath = Path.Combine(storageRootPathProvider.GetStorageRootPath(), "repo");
        var stagingPath = $"{repoPath}.sync-{Guid.NewGuid():N}";
        var repoUrl = await globalSettingsService.GetSettingValueAsync(SettingsMap.HowToCookRepoUrl);
        var backupUrl = await globalSettingsService.GetSettingValueAsync(SettingsMap.HowToCookRepoBackupUrl);

        await SyncSemaphore.WaitAsync();

        try
        {
            Directory.CreateDirectory(stagingPath);

            try
            {
                logger.LogInformation(
                    "SyncHowToCookRepoJob: shallow-cloning primary URL '{RepoUrl}' into staging.", repoUrl);
                await CloneIntoStaging(stagingPath, repoUrl);
            }
            catch (Exception primaryException) when (
                !string.IsNullOrWhiteSpace(backupUrl) &&
                !string.Equals(backupUrl, repoUrl, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(
                    primaryException,
                    "SyncHowToCookRepoJob: primary URL '{RepoUrl}' failed. Falling back to backup URL '{BackupUrl}'.",
                    repoUrl,
                    backupUrl);
                FolderDeleter.DeleteByForce(stagingPath, keepFolder: true);
                await CloneIntoStaging(stagingPath, backupUrl);
            }

            PromoteStagingRepo(stagingPath, repoPath);
            logger.LogInformation("SyncHowToCookRepoJob: repository was replaced successfully.");
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "SyncHowToCookRepoJob: failed to sync. The existing live repository was preserved.");
            throw;
        }
        finally
        {
            try
            {
                FolderDeleter.DeleteByForce(stagingPath);
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "SyncHowToCookRepoJob: staging directory '{StagingPath}' could not be deleted.",
                    stagingPath);
            }
            finally
            {
                SyncSemaphore.Release();
            }
        }
    }

    private async Task CloneIntoStaging(string stagingPath, string repoUrl)
    {
        await workspaceManager.Clone(
            stagingPath,
            branch: null,
            endPoint: repoUrl,
            cloneMode: CloneMode.Depth1);
    }

    private void PromoteStagingRepo(string stagingPath, string repoPath)
    {
        string? backupPath = null;

        if (Directory.Exists(repoPath))
        {
            backupPath = $"{repoPath}.backup-{Guid.NewGuid():N}";
            Directory.Move(repoPath, backupPath);
        }

        try
        {
            Directory.Move(stagingPath, repoPath);
        }
        catch
        {
            if (backupPath is not null && !Directory.Exists(repoPath))
            {
                Directory.Move(backupPath, repoPath);
            }

            throw;
        }

        if (backupPath is null)
        {
            return;
        }

        try
        {
            FolderDeleter.DeleteByForce(backupPath);
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "SyncHowToCookRepoJob: replacement succeeded, but old repo backup '{BackupPath}' could not be deleted.",
                backupPath);
        }
    }
}
