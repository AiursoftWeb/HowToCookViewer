using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Services.BackgroundJobs;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class OrphanAvatarCleanupJobTests : TestBase
{
    private string CreateAvatarFile(string filename, bool isOld)
    {
        var workspace = GetService<FeatureFoldersProvider>().GetWorkspaceFolder();
        var avatarDir = Path.Combine(workspace, "avatar");
        Directory.CreateDirectory(avatarDir);

        var path = Path.Combine(avatarDir, filename);
        File.WriteAllText(path, "fake-avatar-data");
        if (isOld)
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-8));
        }

        return path;
    }

    private async Task RunJob()
    {
        var job = GetService<OrphanAvatarCleanupJob>();
        await job.ExecuteAsync();
    }

    [TestMethod]
    public async Task OldOrphanAvatarIsDeleted()
    {
        var orphanPath = CreateAvatarFile($"orphan-old-{Guid.NewGuid():N}.png", isOld: true);

        await RunJob();

        Assert.IsFalse(File.Exists(orphanPath), "An old orphan avatar should be deleted.");
    }

    [TestMethod]
    public async Task NewOrphanAvatarIsKeptWithinGracePeriod()
    {
        var freshPath = CreateAvatarFile($"orphan-fresh-{Guid.NewGuid():N}.png", isOld: false);
        try
        {
            await RunJob();

            Assert.IsTrue(File.Exists(freshPath),
                "A freshly uploaded avatar must survive until its user record can be saved.");
        }
        finally
        {
            File.Delete(freshPath);
        }
    }

    [TestMethod]
    public async Task ReferencedAvatarIsNeverDeleted()
    {
        var filename = $"referenced-old-{Guid.NewGuid():N}.png";
        var referencedPath = CreateAvatarFile(filename, isOld: true);
        try
        {
            var userManager = GetService<UserManager<User>>();
            var admin = await userManager.Users.FirstAsync();
            admin.AvatarRelativePath = $"avatar/{filename}";
            var updateResult = await userManager.UpdateAsync(admin);
            Assert.IsTrue(updateResult.Succeeded, "The referenced avatar should be saved to the user record.");

            await RunJob();

            Assert.IsTrue(File.Exists(referencedPath),
                "An avatar referenced by a user must never be deleted, even when it is old.");
        }
        finally
        {
            File.Delete(referencedPath);
        }
    }

    [TestMethod]
    public async Task DefaultAvatarIsNeverDeleted()
    {
        var workspace = GetService<FeatureFoldersProvider>().GetWorkspaceFolder();
        var defaultAvatarPath = Path.Combine(workspace, User.DefaultAvatarPath);
        Directory.CreateDirectory(Path.GetDirectoryName(defaultAvatarPath)!);

        var existedBeforeTest = File.Exists(defaultAvatarPath);
        var originalLastWriteTime = existedBeforeTest
            ? File.GetLastWriteTimeUtc(defaultAvatarPath)
            : default;
        if (!existedBeforeTest)
        {
            File.WriteAllText(defaultAvatarPath, "fake-default-avatar-data");
        }

        File.SetLastWriteTimeUtc(defaultAvatarPath, DateTime.UtcNow.AddHours(-8));
        try
        {
            await RunJob();

            Assert.IsTrue(File.Exists(defaultAvatarPath),
                "The default avatar must be kept even when no user references it.");
        }
        finally
        {
            if (existedBeforeTest)
            {
                File.SetLastWriteTimeUtc(defaultAvatarPath, originalLastWriteTime);
            }
            else
            {
                File.Delete(defaultAvatarPath);
            }
        }
    }
}
