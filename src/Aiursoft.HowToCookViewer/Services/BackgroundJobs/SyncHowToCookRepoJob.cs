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
    public string Name => "Sync HowToCook Repo";

    public string Description =>
        "Resets the local HowToCook git repository to the latest state of the upstream remote.";

    public async Task ExecuteAsync()
    {
        var repoPath = Path.Combine(storageRootPathProvider.GetStorageRootPath(), "repo");
        var repoUrl = await globalSettingsService.GetSettingValueAsync(SettingsMap.HowToCookRepoUrl);
        var backupUrl = await globalSettingsService.GetSettingValueAsync(SettingsMap.HowToCookRepoBackupUrl);

        Directory.CreateDirectory(repoPath);

        try
        {
            logger.LogInformation(
                "SyncHowToCookRepoJob: cloning from primary URL '{RepoUrl}'.", repoUrl);
            await workspaceManager.ResetRepo(repoPath, branch: null, endPoint: repoUrl, cloneMode: CloneMode.Full);
            logger.LogInformation("SyncHowToCookRepoJob: repo is up to date.");
        }
        catch (TimeoutException)
        {
            if (string.IsNullOrWhiteSpace(backupUrl) || backupUrl == repoUrl)
                throw;

            logger.LogWarning(
                "SyncHowToCookRepoJob: primary URL '{RepoUrl}' timed out. Cleaning up and falling back to backup URL '{BackupUrl}'.",
                repoUrl, backupUrl);
            FolderDeleter.DeleteByForce(repoPath, keepFolder: true);
            Directory.CreateDirectory(repoPath);
            await workspaceManager.ResetRepo(repoPath, branch: null, endPoint: backupUrl, cloneMode: CloneMode.Full);
            logger.LogInformation("SyncHowToCookRepoJob: repo is up to date (via backup URL).");
        }
        catch (Exception)
        {
            logger.LogError(
                "SyncHowToCookRepoJob: failed to sync repo. Cleaning up local directory to avoid partial state.");
            FolderDeleter.DeleteByForce(repoPath, keepFolder: true);
            throw;
        }
    }
}
