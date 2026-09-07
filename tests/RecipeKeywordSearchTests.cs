using System.Data.Common;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.MySql;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.HowToCookViewer.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class RecipeKeywordSearchTests
{
    [TestMethod]
    public async Task MultiTermSearch_RanksAndPagesInSqlWithoutLoadingBodies()
    {
        var capture = new CommandCapture();
        await using var db = new SqliteContext(new DbContextOptionsBuilder<SqliteContext>()
            .UseSqlite("Data Source=:memory:").AddInterceptors(capture).Options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var exact = Recipe("egg", "exact");
        exact.Calories = 150;
        exact.Images.Add(new RecipeImage { LogicalPath = "cover.jpg", IsCover = true });
        exact.Images.Add(new RecipeImage { LogicalPath = "step.jpg" });
        var both = Recipe("egg rice", "both");
        var translated = Recipe("米饭", "translated");
        translated.LocalizedRecipes.Add(new LocalizedRecipe { Culture = "en", LocalizedName = "rice", Embedding = new byte[4096], LocalizedSteps = "large translated body" });
        var description = Recipe("soup", "description");
        description.Description = "egg and rice";
        db.Recipes.AddRange(exact, both, translated, description, Recipe("unrelated", "none"));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        capture.Commands.Clear();

        var (first, total) = await RecipeSearchService.SearchAsync(db.Recipes.AsNoTracking(), db, "EGG rice", 1, 2);
        var (second, _) = await RecipeSearchService.SearchAsync(db.Recipes.AsNoTracking(), db, "EGG rice", 2, 2);
        Assert.AreEqual(4, total);
        CollectionAssert.AreEqual(new[] { "egg", "米饭", "egg rice", "soup" }, first.Concat(second).Select(r => r.Name).ToArray());
        Assert.AreEqual(150d, first[0].Calories);
        Assert.HasCount(1, first[0].Images);
        Assert.IsTrue(first.Concat(second).All(r => r.Embedding == null && r.Steps == "" && r.LocalizedRecipes.Count == 0));
        Assert.HasCount(4, capture.Commands); // One count and one bounded card query per page.
        Assert.Contains("LIMIT", capture.Commands[1]);
        Assert.Contains("LIMIT", capture.Commands[3]);
        Assert.IsFalse(capture.Commands.Any(c => c.Contains("\"Embedding\"") || c.Contains("\"Steps\"") || c.Contains("\"LocalizedSteps\"")));

        var (single, singleTotal) = await RecipeSearchService.SearchAsync(db.Recipes.AsNoTracking(), db, "EGG", 1, 12);
        Assert.AreEqual(3, singleTotal);
        Assert.AreEqual("egg", single[0].Name);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => RecipeSearchService.SearchAsync(db.Recipes, db, "egg rice", 1, 12, cancelled.Token));
    }

    [TestMethod]
    public void MySql_TranslatesMultiTermRankingAndBoundedCardProjection()
    {
        using var db = new MySqlContext(new DbContextOptionsBuilder<MySqlContext>()
            .UseMySql("Server=localhost;Database=translation_only;Uid=unused;", new MySqlServerVersion(new Version(9, 7, 0))).Options);
        var sql = RecipeSearchService.BuildRankedQuery(db.Recipes.AsNoTracking(), db, ["egg", "rice"])
            .Skip(12).Take(12).SelectCards().ToQueryString();
        Assert.Contains("LIMIT", sql);
        Assert.Contains("CASE", sql);
        Assert.Contains("EXISTS", sql);
        Assert.DoesNotContain("`Embedding`", sql);
        Assert.DoesNotContain("`LocalizedSteps`", sql);
    }

    private static Recipe Recipe(string name, string path) => new()
    {
        Name = name, Category = "test", FilePath = path, Steps = "large recipe body", Embedding = new byte[4096]
    };

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
