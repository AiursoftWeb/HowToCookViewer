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
public class IndexRecipesJobTests
{
    private string _tempPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "IndexJobTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempPath);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, recursive: true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helper: builds all services from a shared config / db name
    // ─────────────────────────────────────────────────────────────────────────

    private (
        SyncHowToCookRepoJob syncJob,
        StorageRootPathProvider rootProvider,
        FeatureFoldersProvider foldersProvider,
        ILoggerFactory loggerFactory,
        DbContextOptions<InMemoryContext> dbOptions
    ) BuildServices(string dbName)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Storage:Path", _tempPath },
                { $"GlobalSettings:{SettingsMap.HowToCookRepoUrl}", "https://github.com/Anduin2017/HowToCook.git" }
            })
            .Build();

        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var rootProvider = new StorageRootPathProvider(config);
        var foldersProvider = new FeatureFoldersProvider(rootProvider);
        var fileLockProvider = new FileLockProvider(memoryCache);
        var storageService = new StorageService(foldersProvider, fileLockProvider, new EphemeralDataProtectionProvider());

        var dbOptions = new DbContextOptionsBuilder<InMemoryContext>()
            .UseInMemoryDatabase(dbName)
            .Options;

        var sp = new ServiceCollection()
            .AddLogging()
            .AddGitRunner()
            .BuildServiceProvider();

        var loggerFactory = sp.GetRequiredService<ILoggerFactory>();

        var globalSettings = new GlobalSettingsService(
            new InMemoryContext(dbOptions), config, storageService, memoryCache);

        var workspaceManager = sp.GetRequiredService<WorkspaceManager>();
        var syncJob = new SyncHowToCookRepoJob(
            rootProvider, globalSettings, workspaceManager,
            loggerFactory.CreateLogger<SyncHowToCookRepoJob>());

        return (syncJob, rootProvider, foldersProvider, loggerFactory, dbOptions);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task IndexRecipesJob_SecondRun_WritesNothing()
    {
        var dbName = "IndexJobTest_" + Guid.NewGuid();
        var (syncJob, rootProvider, foldersProvider, loggerFactory, dbOptions) = BuildServices(dbName);

        // ── Step 1: sync git repo ────────────────────────────────────────────
        await syncJob.ExecuteAsync();

        var repoPath = Path.Combine(_tempPath, "repo");
        Assert.IsTrue(Directory.Exists(repoPath), "Repo must exist before indexing");

        // ── Step 2: first index run — should insert recipes ──────────────────
        await using (var db = new InMemoryContext(dbOptions))
        {
            var job = new IndexRecipesJob(
                rootProvider, foldersProvider, db,
                loggerFactory.CreateLogger<IndexRecipesJob>());
            await job.ExecuteAsync();
        }

        int recipeCount;
        await using (var db = new InMemoryContext(dbOptions))
        {
            recipeCount = await db.Recipes.CountAsync();
        }
        Assert.IsTrue(recipeCount > 0,
            "First run must insert at least one recipe into the database.");

        // ── Step 3: second index run — must write NOTHING ────────────────────
        await using var strictDb = new NoWriteDbContext(dbOptions);
        var job2 = new IndexRecipesJob(
            rootProvider, foldersProvider, strictDb,
            loggerFactory.CreateLogger<IndexRecipesJob>());

        // This must not throw: NoWriteDbContext throws if SaveChanges has
        // any pending Added / Modified / Deleted entries.
        await job2.ExecuteAsync();

        // Sanity: record count must be identical after second run
        await using (var db = new InMemoryContext(dbOptions))
        {
            var countAfter = await db.Recipes.CountAsync();
            Assert.AreEqual(recipeCount, countAfter,
                "Second run must not add, update, or remove any recipes.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Strict context: throws immediately if SaveChangesAsync is called with
    // any pending writes (Added / Modified / Deleted entries).
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class NoWriteDbContext(DbContextOptions<InMemoryContext> options)
        : InMemoryContext(options)
    {
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var dirty = ChangeTracker.Entries()
                .Where(e => e.State is EntityState.Added
                                    or EntityState.Modified
                                    or EntityState.Deleted)
                .ToList();

            if (dirty.Count > 0)
            {
                var details = string.Join(", ",
                    dirty.Select(e => $"{e.Entity.GetType().Name}({e.State})"));
                throw new InvalidOperationException(
                    $"Second IndexRecipesJob run attempted to write {dirty.Count} change(s): {details}. " +
                    "The incremental sync should have detected no changes and skipped all recipes.");
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }
}
