using Aiursoft.HowToCookViewer.Models.SystemViewModels;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class MigrationEntryTests
{
    [TestMethod]
    public void Name_ParsedCorrectly_FromStandardId()
    {
        var entry = new MigrationEntry { Id = "20260108110700_AddGlobalSettings" };
        Assert.AreEqual("AddGlobalSettings", entry.Name);
    }

    [TestMethod]
    public void Name_ReturnsFullId_WhenShorterThan15Chars()
    {
        var entry = new MigrationEntry { Id = "ShortId" };
        Assert.AreEqual("ShortId", entry.Name);
    }

    [TestMethod]
    public void AppliedAt_ParsedCorrectly_FromStandardId()
    {
        var entry = new MigrationEntry { Id = "20260108110700_AddGlobalSettings" };
        var expected = new DateTime(2026, 1, 8, 11, 7, 0, DateTimeKind.Utc);
        Assert.AreEqual(expected, entry.AppliedAt);
    }

    [TestMethod]
    public void AppliedAt_ReturnsNull_WhenTimestampIsInvalid()
    {
        var entry = new MigrationEntry { Id = "NotATimestamp_SomeMigration" };
        Assert.IsNull(entry.AppliedAt);
    }

    [TestMethod]
    public void AppliedAt_ReturnsNull_WhenIdIsTooShort()
    {
        var entry = new MigrationEntry { Id = "Short" };
        Assert.IsNull(entry.AppliedAt);
    }

    [TestMethod]
    [DataRow("20250826125833_Init", 2025, 8, 26, 12, 58, 33, "Init")]
    [DataRow("20250911113624_AddDisplayName", 2025, 9, 11, 11, 36, 24, "AddDisplayName")]
    [DataRow("20260108110700_AddGlobalSettings", 2026, 1, 8, 11, 7, 0, "AddGlobalSettings")]
    public void NameAndTimestamp_MatchKnownMigrationIds(
        string id, int year, int month, int day, int hour, int minute, int second, string expectedName)
    {
        var entry = new MigrationEntry { Id = id };
        Assert.AreEqual(expectedName, entry.Name);
        Assert.AreEqual(new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc), entry.AppliedAt);
    }
}
