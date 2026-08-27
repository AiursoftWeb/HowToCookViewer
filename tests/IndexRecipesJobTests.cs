using Aiursoft.GitRunner;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.InMemory;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Services.BackgroundJobs;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class IndexRecipesJobTests
{
    private string _tempPath = null!;
    private string _mockRepoPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "IndexJobTest_" + Guid.NewGuid());
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
        File.WriteAllText(Path.Combine(dishesPath, "tomato.md"),
            "# Tomato\nA simple tomato dish.\n预估卡路里：468大卡\n## 必备原料和工具\n- Tomato\n## 计算\n- 1 serving\n## 操作\n1. Cook\n## 附加内容\nServe hot.");
        var tipsPath = Path.Combine(path, "tips", "learn");
        Directory.CreateDirectory(tipsPath);
        File.WriteAllText(Path.Combine(tipsPath, "heat.md"), "# Heat\nUse medium heat.");

        RunGitCommand("init --initial-branch=main", path);
        RunGitCommand("add .", path);
        // Use -c overrides so global GPG / hook settings don't break the test.
        RunGitCommand(
            "-c user.name=TestUser -c user.email=test@test.com -c commit.gpgsign=false commit --no-gpg-sign -m \"Initial commit\"",
            path,
            "2026-01-01T00:00:00Z");
    }

    private static string RunGitCommand(string args, string path, string? commitDate = null)
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
        if (commitDate != null)
        {
            p.StartInfo.Environment["GIT_AUTHOR_DATE"] = commitDate;
            p.StartInfo.Environment["GIT_COMMITTER_DATE"] = commitDate;
        }

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

        return p.StandardOutput.ReadToEnd().Trim();
    }

    private void CommitUpstreamChange(string message, string commitDate)
    {
        RunGitCommand("add .", _mockRepoPath);
        RunGitCommand(
            $"-c user.name=TestUser -c user.email=test@test.com -c commit.gpgsign=false commit --no-gpg-sign -m \"{message}\"",
            _mockRepoPath,
            commitDate);
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
                // file:// is required for git to honor --depth=1 for a local test remote.
                { $"GlobalSettings:{SettingsMap.HowToCookRepoUrl}", new Uri(_mockRepoPath + Path.DirectorySeparatorChar).AbsoluteUri }
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
            var recipe = await db.Recipes.FirstAsync();
            Assert.AreEqual(468, recipe.Calories,
                "First run must parse and store calorie value from markdown.");
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
    // Test: recipes indexed before calorie feature was added must be re-indexed
    // ─────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task IndexRecipesJob_ReindexesWhenCaloriesNull()
    {
        var dbName = "IndexJobTest_" + Guid.NewGuid();
        var (syncJob, rootProvider, foldersProvider, loggerFactory, dbOptions) = BuildServices(dbName);

        // ── Step 1: sync and index normally ────────────────────────────────────
        await syncJob.ExecuteAsync();

        await using (var db = new InMemoryContext(dbOptions))
        {
            var job = new IndexRecipesJob(
                rootProvider, foldersProvider, db,
                loggerFactory.CreateLogger<IndexRecipesJob>());
            await job.ExecuteAsync();
        }

        // ── Step 2: simulate pre-calorie-feature state by clearing Calories ────
        await using (var db = new InMemoryContext(dbOptions))
        {
            var recipe = await db.Recipes.FirstAsync();
            Assert.AreEqual(468, recipe.Calories,
                "First run must parse and store calorie value.");

            recipe.Calories = null;
            await db.SaveChangesAsync();
        }

        // ── Step 3: re-run index — must restore the calorie value ─────────────
        await using (var db = new InMemoryContext(dbOptions))
        {
            var job = new IndexRecipesJob(
                rootProvider, foldersProvider, db,
                loggerFactory.CreateLogger<IndexRecipesJob>());
            await job.ExecuteAsync();
        }

        await using (var db = new InMemoryContext(dbOptions))
        {
            var recipe = await db.Recipes.FirstAsync();
            Assert.AreEqual(468, recipe.Calories,
                "Second run must re-index and restore calorie value for recipes that had null calories.");
        }
    }

    [TestMethod]
    public async Task IndexRecipesJob_ShallowCloneHeadMove_DoesNotInvalidateUnchangedRecipe()
    {
        var dbName = "IndexJobTest_" + Guid.NewGuid();
        var (syncJob, rootProvider, foldersProvider, loggerFactory, dbOptions) = BuildServices(dbName);

        await syncJob.ExecuteAsync();
        await using (var db = new InMemoryContext(dbOptions))
        {
            var job = new IndexRecipesJob(
                rootProvider, foldersProvider, db,
                loggerFactory.CreateLogger<IndexRecipesJob>());
            await job.ExecuteAsync();
        }

        DateTime originalTranslationRevision;
        await using (var db = new InMemoryContext(dbOptions))
        {
            originalTranslationRevision = (await db.Recipes.FirstAsync()).FileLastModified;
        }

        File.WriteAllText(Path.Combine(_mockRepoPath, "README.md"), "Unrelated documentation update.");
        CommitUpstreamChange("Update README", "2026-01-02T00:00:00Z");
        await syncJob.ExecuteAsync();

        var clonedRepoPath = Path.Combine(_tempPath, "repo");
        Assert.AreEqual("true", RunGitCommand("rev-parse --is-shallow-repository", clonedRepoPath),
            "This regression test must exercise a real depth-1 clone.");
        var shallowPathTimestamp = DateTimeOffset.Parse(
            RunGitCommand("log -1 --format=%cI -- dishes/vegetable_dish/tomato.md", clonedRepoPath)).UtcDateTime;
        Assert.IsTrue(shallowPathTimestamp > originalTranslationRevision,
            "A depth-1 clone should incorrectly report the new HEAD timestamp for the unchanged recipe path.");

        await using (var strictDb = new NoWriteDbContext(dbOptions))
        {
            var job = new IndexRecipesJob(
                rootProvider, foldersProvider, strictDb,
                loggerFactory.CreateLogger<IndexRecipesJob>());
            await job.ExecuteAsync();
        }

        await using (var db = new InMemoryContext(dbOptions))
        {
            Assert.AreEqual(originalTranslationRevision, (await db.Recipes.FirstAsync()).FileLastModified,
                "An unrelated upstream commit must not invalidate an existing translation.");
        }
    }

    [TestMethod]
    public async Task IndexRecipesJob_MetadataOnlyChange_DoesNotInvalidateTranslation()
    {
        var dbName = "IndexJobTest_" + Guid.NewGuid();
        var (syncJob, rootProvider, foldersProvider, loggerFactory, dbOptions) = BuildServices(dbName);

        await syncJob.ExecuteAsync();
        await using (var db = new InMemoryContext(dbOptions))
        {
            var job = new IndexRecipesJob(
                rootProvider, foldersProvider, db,
                loggerFactory.CreateLogger<IndexRecipesJob>());
            await job.ExecuteAsync();
        }

        DateTime originalTranslationRevision;
        await using (var db = new InMemoryContext(dbOptions))
        {
            originalTranslationRevision = (await db.Recipes.FirstAsync()).FileLastModified;
        }

        File.WriteAllText(Path.Combine(_mockRepoPath, "dishes", "vegetable_dish", "tomato.md"),
            "# Tomato\nA simple tomato dish.\n预估卡路里：500大卡\n## 必备原料和工具\n- Tomato\n## 计算\n- 1 serving\n## 操作\n1. Cook\n## 附加内容\nServe hot.");
        CommitUpstreamChange("Correct calories", "2026-01-02T00:00:00Z");
        await syncJob.ExecuteAsync();

        await using (var db = new InMemoryContext(dbOptions))
        {
            var job = new IndexRecipesJob(
                rootProvider, foldersProvider, db,
                loggerFactory.CreateLogger<IndexRecipesJob>());
            await job.ExecuteAsync();
        }

        await using (var db = new InMemoryContext(dbOptions))
        {
            var recipe = await db.Recipes.FirstAsync();
            Assert.AreEqual(500, recipe.Calories, "Metadata changes must still be indexed.");
            Assert.AreEqual(originalTranslationRevision, recipe.FileLastModified,
                "A calorie-only change must not invalidate translations.");
        }
    }

    [TestMethod]
    public async Task IndexRecipesJob_TranslatableChange_AdvancesTranslationRevision()
    {
        var dbName = "IndexJobTest_" + Guid.NewGuid();
        var (syncJob, rootProvider, foldersProvider, loggerFactory, dbOptions) = BuildServices(dbName);

        await syncJob.ExecuteAsync();
        await using (var db = new InMemoryContext(dbOptions))
        {
            var job = new IndexRecipesJob(
                rootProvider, foldersProvider, db,
                loggerFactory.CreateLogger<IndexRecipesJob>());
            await job.ExecuteAsync();
        }

        DateTime originalTranslationRevision;
        await using (var db = new InMemoryContext(dbOptions))
        {
            originalTranslationRevision = (await db.Recipes.FirstAsync()).FileLastModified;
        }

        File.WriteAllText(Path.Combine(_mockRepoPath, "dishes", "vegetable_dish", "tomato.md"),
            "# Tomato\nA simple tomato dish.\n预估卡路里：468大卡\n## 必备原料和工具\n- Tomato\n## 计算\n- 1 serving\n## 操作\n1. Cook gently\n## 附加内容\nServe hot.");
        CommitUpstreamChange("Improve cooking step", "2026-01-02T00:00:00Z");
        await syncJob.ExecuteAsync();

        await using (var db = new InMemoryContext(dbOptions))
        {
            var job = new IndexRecipesJob(
                rootProvider, foldersProvider, db,
                loggerFactory.CreateLogger<IndexRecipesJob>());
            await job.ExecuteAsync();
        }

        await using (var db = new InMemoryContext(dbOptions))
        {
            var recipe = await db.Recipes.FirstAsync();
            Assert.AreEqual("1. Cook gently", recipe.Steps);
            Assert.IsTrue(recipe.FileLastModified > originalTranslationRevision,
                "A changed translation source must advance the revision and invalidate old translations.");
        }
    }

    [TestMethod]
    public async Task IndexTipsJob_ShallowCloneHeadMove_DoesNotInvalidateUnchangedTip()
    {
        var dbName = "IndexJobTest_" + Guid.NewGuid();
        var (syncJob, rootProvider, _, loggerFactory, dbOptions) = BuildServices(dbName);

        await syncJob.ExecuteAsync();
        await using (var db = new InMemoryContext(dbOptions))
        {
            var job = new IndexTipsJob(
                rootProvider, db,
                loggerFactory.CreateLogger<IndexTipsJob>());
            await job.ExecuteAsync();
        }

        DateTime originalTranslationRevision;
        await using (var db = new InMemoryContext(dbOptions))
        {
            originalTranslationRevision = (await db.Tips.FirstAsync()).FileLastModified;
        }

        File.WriteAllText(Path.Combine(_mockRepoPath, "README.md"), "Unrelated documentation update.");
        CommitUpstreamChange("Update README", "2026-01-02T00:00:00Z");
        await syncJob.ExecuteAsync();

        await using (var strictDb = new NoWriteDbContext(dbOptions))
        {
            var job = new IndexTipsJob(
                rootProvider, strictDb,
                loggerFactory.CreateLogger<IndexTipsJob>());
            await job.ExecuteAsync();
        }

        await using (var db = new InMemoryContext(dbOptions))
        {
            Assert.AreEqual(originalTranslationRevision, (await db.Tips.FirstAsync()).FileLastModified,
                "An unrelated shallow-clone HEAD must not invalidate tip translations.");
        }
    }

    [TestMethod]
    public async Task IndexTipsJob_TranslatableChange_AdvancesTranslationRevision()
    {
        var dbName = "IndexJobTest_" + Guid.NewGuid();
        var (syncJob, rootProvider, _, loggerFactory, dbOptions) = BuildServices(dbName);

        await syncJob.ExecuteAsync();
        await using (var db = new InMemoryContext(dbOptions))
        {
            var job = new IndexTipsJob(
                rootProvider, db,
                loggerFactory.CreateLogger<IndexTipsJob>());
            await job.ExecuteAsync();
        }

        DateTime originalTranslationRevision;
        await using (var db = new InMemoryContext(dbOptions))
        {
            originalTranslationRevision = (await db.Tips.FirstAsync()).FileLastModified;
        }

        File.WriteAllText(Path.Combine(_mockRepoPath, "tips", "learn", "heat.md"),
            "# Heat\nUse low heat.");
        CommitUpstreamChange("Correct heat tip", "2026-01-02T00:00:00Z");
        await syncJob.ExecuteAsync();

        await using (var db = new InMemoryContext(dbOptions))
        {
            var job = new IndexTipsJob(
                rootProvider, db,
                loggerFactory.CreateLogger<IndexTipsJob>());
            await job.ExecuteAsync();
        }

        await using (var db = new InMemoryContext(dbOptions))
        {
            var tip = await db.Tips.FirstAsync();
            Assert.AreEqual("# Heat\nUse low heat.", tip.Content);
            Assert.IsTrue(tip.FileLastModified > originalTranslationRevision,
                "Changed tip content must invalidate the old translation.");
        }
    }

    [TestMethod]
    public async Task ShallowSyncThroughLocalization_InvokesTranslatorOnlyForTranslatableChanges()
    {
        var syncDbName = "IndexJobTest_" + Guid.NewGuid();
        var (syncJob, rootProvider, foldersProvider, loggerFactory, _) = BuildServices(syncDbName);

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var sqliteOptions = new DbContextOptionsBuilder<SqliteEndToEndContext>()
            .UseSqlite(connection)
            .Options;

        await using (var db = new SqliteEndToEndContext(sqliteOptions))
        {
            await db.Database.EnsureCreatedAsync();
            db.GlobalSettings.AddRange(
                new GlobalSetting
                {
                    Key = SettingsMap.LocalizationLanguages,
                    Value = "en-US"
                },
                new GlobalSetting
                {
                    Key = SettingsMap.OpenAiInstance,
                    Value = "http://localhost:11434"
                });
            await db.SaveChangesAsync();
        }

        // Index the initial recipe, then seed a translation completed after that revision.
        await syncJob.ExecuteAsync();
        await using (var db = new SqliteEndToEndContext(sqliteOptions))
        {
            var indexJob = new IndexRecipesJob(
                rootProvider, foldersProvider, db,
                loggerFactory.CreateLogger<IndexRecipesJob>());
            await indexJob.ExecuteAsync();

            var recipe = await db.Recipes.FirstAsync();
            db.LocalizedRecipes.Add(new LocalizedRecipe
            {
                RecipeId = recipe.Id,
                Culture = "en-US",
                LocalizedName = "Current name",
                LocalizedDescription = "Current description",
                LocalizedIngredients = "Current ingredients",
                LocalizedCalculation = "Current calculation",
                LocalizedSteps = "Current steps",
                LocalizedNotes = "Current notes",
                LastLocalizedAt = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }

        var translator = new CountingTranslationService();

        // An unrelated commit becomes HEAD in the next depth-1 clone. It must not
        // reach the translator even though git now attributes that HEAD to the recipe path.
        File.WriteAllText(Path.Combine(_mockRepoPath, "README.md"), "Unrelated documentation update.");
        CommitUpstreamChange("Update README", "2026-01-02T00:00:00Z");
        await syncJob.ExecuteAsync();
        await RunIndexAndLocalizationAsync(
            sqliteOptions, rootProvider, foldersProvider, loggerFactory, translator);

        Assert.AreEqual(0, translator.CallCount,
            "An unrelated shallow-clone HEAD must result in zero translation calls.");

        // A real change to one of the translated fields must invalidate the recipe.
        File.WriteAllText(Path.Combine(_mockRepoPath, "dishes", "vegetable_dish", "tomato.md"),
            "# Tomato\nA simple tomato dish.\n预估卡路里：468大卡\n## 必备原料和工具\n- Tomato\n## 计算\n- 1 serving\n## 操作\n1. Cook gently\n## 附加内容\nServe hot.");
        CommitUpstreamChange("Improve cooking step", "2026-01-03T00:00:00Z");
        await syncJob.ExecuteAsync();
        await RunIndexAndLocalizationAsync(
            sqliteOptions, rootProvider, foldersProvider, loggerFactory, translator);

        Assert.AreEqual(6, translator.CallCount,
            "A real source change should translate the six non-empty recipe fields exactly once.");
        await using (var db = new SqliteEndToEndContext(sqliteOptions))
        {
            var localized = await db.LocalizedRecipes.FirstAsync();
            StringAssert.Contains(localized.LocalizedSteps, "Cook gently");
        }
    }

    private static async Task RunIndexAndLocalizationAsync(
        DbContextOptions<SqliteEndToEndContext> dbOptions,
        StorageRootPathProvider rootProvider,
        FeatureFoldersProvider foldersProvider,
        ILoggerFactory loggerFactory,
        IRecipeTranslationService translator)
    {
        await using (var indexDb = new SqliteEndToEndContext(dbOptions))
        {
            var indexJob = new IndexRecipesJob(
                rootProvider, foldersProvider, indexDb,
                loggerFactory.CreateLogger<IndexRecipesJob>());
            await indexJob.ExecuteAsync();
        }

        await using var localizeDb = new SqliteEndToEndContext(dbOptions);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var config = new ConfigurationBuilder().Build();
        var settings = new GlobalSettingsService(localizeDb, config, null!, cache);
        var localizeJob = new LocalizeRecipesJob(
            localizeDb, settings, translator,
            loggerFactory.CreateLogger<LocalizeRecipesJob>());
        await localizeJob.ExecuteAsync();
    }

    private sealed class SqliteEndToEndContext(DbContextOptions<SqliteEndToEndContext> options)
        : TemplateDbContext(options);

    private sealed class CountingTranslationService : IRecipeTranslationService
    {
        public int CallCount { get; private set; }

        public Task<string> TranslateAsync(string text, string targetLanguage)
        {
            CallCount++;
            return Task.FromResult($"[{targetLanguage}] {text}");
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
