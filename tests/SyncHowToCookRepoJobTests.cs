using Aiursoft.GitRunner;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.InMemory;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Services.BackgroundJobs;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class SyncHowToCookRepoJobTests
{
    private string _tempPath = null!;
    private string _mockRepoPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "SyncJobTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempPath);

        _mockRepoPath = Path.Combine(Path.GetTempPath(), "MockRepo_" + Guid.NewGuid());
        CreateMockGitRepo(_mockRepoPath);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, recursive: true);
        if (Directory.Exists(_mockRepoPath))
            Directory.Delete(_mockRepoPath, recursive: true);
    }

    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);

    private void CreateMockGitRepo(string path)
    {
        Directory.CreateDirectory(path);
        var dishesPath = Path.Combine(path, "dishes", "vegetable_dish");
        Directory.CreateDirectory(dishesPath);
        File.WriteAllText(Path.Combine(dishesPath, "tomato.md"), "# Tomato\n## Ingredients\n- Tomato\n## Steps\n1. Cook");

        RunGitCommand("init --initial-branch=main", path);
        RunGitCommand("add .", path);
        // Use -c overrides so global GPG / hook settings don't break the test.
        RunGitCommand("-c user.name=TestUser -c user.email=test@test.com -c commit.gpgsign=false commit --no-gpg-sign -m \"Initial commit\"", path);
    }

    private static void RunGitCommand(string args, string path)
    {
        var p = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = args,
                WorkingDirectory = path,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        p.Start();
        if (!p.WaitForExit(GitTimeout))
        {
            p.Kill(entireProcessTree: true);
            throw new TimeoutException($"git {args} timed out after {GitTimeout.TotalSeconds}s.");
        }
        if (p.ExitCode != 0)
        {
            throw new Exception($"git {args} failed (exit {p.ExitCode}): {p.StandardError.ReadToEnd()}");
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_ClonesRepoToCorrectPath()
    {
        // Arrange
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Storage:Path", _tempPath },
                // Supply via config so GlobalSettingsService returns early — no DB access needed.
                { $"GlobalSettings:{SettingsMap.HowToCookRepoUrl}", _mockRepoPath }
            })
            .Build();

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var rootProvider = new StorageRootPathProvider(config);
        var foldersProvider = new FeatureFoldersProvider(rootProvider);
        var fileLockProvider = new FileLockProvider(memoryCache);
        var storageService = new StorageService(foldersProvider, fileLockProvider, new EphemeralDataProtectionProvider());

        var dbOptions = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase("SyncJobTest_" + Guid.NewGuid())
            .Options;
        await using var db = new InMemoryContext(dbOptions);

        var globalSettings = new GlobalSettingsService(db, config, storageService, memoryCache);

        var sp = new ServiceCollection()
            .AddLogging()
            .AddGitRunner()
            .BuildServiceProvider();
        var workspaceManager = sp.GetRequiredService<WorkspaceManager>();

        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<SyncHowToCookRepoJob>();
        var job = new SyncHowToCookRepoJob(rootProvider, globalSettings, workspaceManager, logger);

        // Act
        await job.ExecuteAsync();

        // Assert
        var expectedRepoPath = Path.Combine(_tempPath, "repo");
        Assert.IsTrue(
            Directory.Exists(expectedRepoPath),
            $"Repo directory should exist at: {expectedRepoPath}");
        Assert.IsTrue(
            Directory.Exists(Path.Combine(expectedRepoPath, ".git")),
            "Cloned repo should contain a .git directory");
    }

    [TestMethod]
    public async Task ExecuteAsync_ResetsRepoWhenAlreadyCloned()
    {
        // Arrange — first clone
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Storage:Path", _tempPath },
                { $"GlobalSettings:{SettingsMap.HowToCookRepoUrl}", _mockRepoPath }
            })
            .Build();

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var rootProvider = new StorageRootPathProvider(config);
        var foldersProvider = new FeatureFoldersProvider(rootProvider);
        var fileLockProvider = new FileLockProvider(memoryCache);
        var storageService = new StorageService(foldersProvider, fileLockProvider, new EphemeralDataProtectionProvider());

        var dbOptions = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase("SyncJobTest_" + Guid.NewGuid())
            .Options;
        await using var db = new InMemoryContext(dbOptions);
        var globalSettings = new GlobalSettingsService(db, config, storageService, memoryCache);

        var sp = new ServiceCollection()
            .AddLogging()
            .AddGitRunner()
            .BuildServiceProvider();
        var workspaceManager = sp.GetRequiredService<WorkspaceManager>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<SyncHowToCookRepoJob>();
        var job = new SyncHowToCookRepoJob(rootProvider, globalSettings, workspaceManager, logger);

        await job.ExecuteAsync(); // first run — clones

        // Dirty the working tree with a stray file
        var strayFile = Path.Combine(_tempPath, "repo", "stray-test-file.txt");
        await File.WriteAllTextAsync(strayFile, "should be cleaned up");

        // Act — second run should reset/clean the repo
        await job.ExecuteAsync();

        // Assert — stray file removed, repo still valid
        Assert.IsFalse(File.Exists(strayFile), "Stray file should have been removed by git clean");
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempPath, "repo", ".git")));
    }

    [TestMethod]
    public async Task ExecuteAsync_FallsBackToBackupRepoWhenPrimaryFails()
    {
        // Arrange
        var missingPrimary = Path.Combine(_tempPath, "missing-primary.git");
        var job = CreateJob(missingPrimary, _mockRepoPath);

        // Act
        await job.ExecuteAsync();

        // Assert
        var repoPath = Path.Combine(_tempPath, "repo");
        Assert.IsTrue(Directory.Exists(Path.Combine(repoPath, ".git")));
        Assert.IsTrue(File.Exists(Path.Combine(repoPath, "dishes", "vegetable_dish", "tomato.md")));
    }

    [TestMethod]
    public async Task ExecuteAsync_PreservesLiveRepoWhenBothRemotesFail()
    {
        // Arrange
        var liveRepoPath = Path.Combine(_tempPath, "repo");
        Directory.CreateDirectory(liveRepoPath);
        var sentinelPath = Path.Combine(liveRepoPath, "existing-content.txt");
        await File.WriteAllTextAsync(sentinelPath, "keep me");

        var missingPrimary = Path.Combine(_tempPath, "missing-primary.git");
        var missingBackup = Path.Combine(_tempPath, "missing-backup.git");
        var job = CreateJob(missingPrimary, missingBackup);

        // Act
        await Assert.ThrowsAsync<Exception>(() => job.ExecuteAsync());

        // Assert
        Assert.IsTrue(File.Exists(sentinelPath), "The existing live repo must survive a failed sync.");
        Assert.AreEqual("keep me", await File.ReadAllTextAsync(sentinelPath));
        Assert.IsFalse(
            Directory.GetDirectories(_tempPath, "repo.sync-*", SearchOption.TopDirectoryOnly).Any(),
            "Failed staging directories should be cleaned up.");
    }

    private SyncHowToCookRepoJob CreateJob(string primaryUrl, string? backupUrl = null)
    {
        var settings = new Dictionary<string, string?>
        {
            { "Storage:Path", _tempPath },
            { $"GlobalSettings:{SettingsMap.HowToCookRepoUrl}", primaryUrl }
        };

        if (backupUrl is not null)
        {
            settings[$"GlobalSettings:{SettingsMap.HowToCookRepoBackupUrl}"] = backupUrl;
        }

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var rootProvider = new StorageRootPathProvider(config);
        var foldersProvider = new FeatureFoldersProvider(rootProvider);
        var fileLockProvider = new FileLockProvider(memoryCache);
        var storageService = new StorageService(
            foldersProvider,
            fileLockProvider,
            new EphemeralDataProtectionProvider());

        var dbOptions = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase("SyncJobTest_" + Guid.NewGuid())
            .Options;
        var db = new InMemoryContext(dbOptions);
        var globalSettings = new GlobalSettingsService(db, config, storageService, memoryCache);

        var serviceProvider = new ServiceCollection()
            .AddLogging()
            .AddGitRunner()
            .BuildServiceProvider();
        var workspaceManager = serviceProvider.GetRequiredService<WorkspaceManager>();
        var logger = serviceProvider.GetRequiredService<ILoggerFactory>().CreateLogger<SyncHowToCookRepoJob>();

        return new SyncHowToCookRepoJob(rootProvider, globalSettings, workspaceManager, logger);
    }
}
