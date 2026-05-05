using Aiursoft.Canon.BackgroundJobs;
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
        logger.LogInformation(
            "SyncHowToCookRepoJob: resetting repo at '{RepoPath}' from '{RepoUrl}'.",
            repoPath, repoUrl);

        Directory.CreateDirectory(repoPath);
        await workspaceManager.ResetRepo(repoPath, branch: null, endPoint: repoUrl, cloneMode: CloneMode.Full);

        logger.LogInformation("SyncHowToCookRepoJob: repo is up to date.");
    }
}
