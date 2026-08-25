using Aiursoft.HowToCookViewer.Services.FileStorage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;

namespace Aiursoft.HowToCookViewer.Tests;

[TestClass]
public class FileAccessSecurityTest
{
    private StorageService _storageService = null!;
    private string _tempPath = null!;

    [TestInitialize]
    public void Initialize()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), "AiursoftTemplateTest_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempPath);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Storage:Path", _tempPath },
                { "Storage:Key", "test-key" }
            })
            .Build();

        var rootProvider = new StorageRootPathProvider(config);
        var foldersProvider = new FeatureFoldersProvider(rootProvider);
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var fileLockProvider = new FileLockProvider(memoryCache);
        var dataProtectionProvider = new EphemeralDataProtectionProvider();

        _storageService = new StorageService(foldersProvider, fileLockProvider, dataProtectionProvider);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempPath))
        {
            Directory.Delete(_tempPath, true);
        }
    }

    [TestMethod]
    public void TestGetFilePhysicalPath_NormalAccess()
    {
        var relativePath = "test.txt";
        var physicalPath = _storageService.GetFilePhysicalPath(relativePath);

        StringAssert.StartsWith(physicalPath, _tempPath);
        StringAssert.EndsWith(physicalPath, relativePath);
    }

    [TestMethod]
    public void TestGetFilePhysicalPath_VaultAccess()
    {
        var relativePath = "private.txt";
        var physicalPath = _storageService.GetFilePhysicalPath(relativePath, isVault: true);

        StringAssert.StartsWith(physicalPath, _tempPath);
        StringAssert.Contains(physicalPath, "Vault");
        StringAssert.EndsWith(physicalPath, relativePath);
    }

    [TestMethod]
    [DataRow("../secret.txt")]
    [DataRow("../../etc/passwd")]
    [DataRow("/etc/passwd")]
    [DataRow("../WorkspaceBackup/secret.txt")]
    [DataRow("folder/../secret.txt")]
    [DataRow("folder\\..\\secret.txt")]
    public void TestGetFilePhysicalPath_PathTraversal(string maliciousPath)
    {
        try
        {
            _storageService.GetFilePhysicalPath(maliciousPath);
            Assert.Fail("Expected ArgumentException was not thrown.");
        }
        catch (ArgumentException)
        {
            // Expected
        }
    }

    [TestMethod]
    public async Task TestSave_NormalAccess()
    {
        var content = "Hello World";
        var fileName = "test_upload.txt";
        var ms = new MemoryStream();
        var writer = new StreamWriter(ms);
        writer.Write(content);
        writer.Flush();
        ms.Position = 0;

        var formFile = new FormFile(ms, 0, ms.Length, "file", fileName);

        var savedPath = await _storageService.Save("uploads/" + fileName, formFile);

        StringAssert.Contains(savedPath, "uploads");
        StringAssert.Contains(savedPath, fileName);
    }

    [TestMethod]
    [DataRow("../malicious.txt")]
    [DataRow("../../malicious.txt")]
    [DataRow("/absolute/path/malicious.txt")]
    public async Task TestSave_PathTraversal(string maliciousPath)
    {
        var ms = new MemoryStream();
        var formFile = new FormFile(ms, 0, 0, "file", "dummy.txt");

        try
        {
            await _storageService.Save(maliciousPath, formFile);
            Assert.Fail("Expected ArgumentException was not thrown.");
        }
        catch (ArgumentException)
        {
            // Expected
        }
    }

    [TestMethod]
    public async Task TestSave_Collision()
    {
        var fileName = "collision.txt";
        var content1 = "Content 1";
        var ms1 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content1));
        var formFile1 = new FormFile(ms1, 0, ms1.Length, "file", fileName);

        var path1 = await _storageService.Save(fileName, formFile1);
        Assert.AreEqual(fileName, path1);

        var content2 = "Content 2";
        var ms2 = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content2));
        var formFile2 = new FormFile(ms2, 0, ms2.Length, "file", fileName);

        var path2 = await _storageService.Save(fileName, formFile2);
        Assert.AreEqual("_" + fileName, path2);
    }

    [TestMethod]
    public async Task TestSaveFromStream_NormalAccess()
    {
        await using var stream = new MemoryStream("Hello Stream"u8.ToArray());

        var savedPath = await _storageService.SaveFromStream("uploads/stream.txt", stream);

        Assert.AreEqual("uploads/stream.txt", savedPath);
        Assert.AreEqual(
            "Hello Stream",
            await File.ReadAllTextAsync(_storageService.GetFilePhysicalPath(savedPath)));
    }

    [TestMethod]
    public async Task TestSaveFromStream_Collision()
    {
        await using var first = new MemoryStream("first"u8.ToArray());
        await using var second = new MemoryStream("second"u8.ToArray());

        var firstPath = await _storageService.SaveFromStream("stream.txt", first);
        var secondPath = await _storageService.SaveFromStream("stream.txt", second);

        Assert.AreEqual("stream.txt", firstPath);
        Assert.AreEqual("_stream.txt", secondPath);
    }

    [TestMethod]
    [DataRow("../malicious.txt")]
    [DataRow("/absolute/path/malicious.txt")]
    public async Task TestSaveFromStream_PathTraversal(string maliciousPath)
    {
        await using var stream = new MemoryStream("content"u8.ToArray());

        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await _storageService.SaveFromStream(maliciousPath, stream));
    }

    [TestMethod]
    public void TestGetVaultSubfolderFilePhysicalPath_RejectsSiblingFolder()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            _storageService.GetVaultSubfolderFilePhysicalPath("audio-backup/secret.mp3", "audio"));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("/")]
    [DataRow("audio/../secret.mp3")]
    public void TestRelativePathToInternetUrl_InvalidPathHasNoUrl(string invalidPath)
    {
        Assert.AreEqual(string.Empty, _storageService.RelativePathToInternetUrl(invalidPath));
    }

    [TestMethod]
    public void TestValidateToken_Success()
    {
        var path = "test-folder";
        var token = _storageService.GetToken(path, FilePermission.Upload);
        var isValid = _storageService.ValidateToken(path, token, FilePermission.Upload);
        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public void TestValidateToken_SubfolderSuccess()
    {
        var path = "parent";
        var token = _storageService.GetToken(path, FilePermission.Upload);
        var isValid = _storageService.ValidateToken("parent/child", token, FilePermission.Upload);
        Assert.IsTrue(isValid);
    }

    [TestMethod]
    public void TestValidateToken_SitePrefixVulnerability()
    {
        // Arrange: Hacker creates a site named "A"
        var hackerSite = "A";
        var token = _storageService.GetToken(hackerSite, FilePermission.Upload);

        // Act: Hacker tries to upload to "AA" (victim's site) using the token for "A"
        var victimSite = "AA";
        var isValid = _storageService.ValidateToken(victimSite, token, FilePermission.Upload);

        // Assert: Access should be DENIED
        // This assertion is expected to FAIL until the vulnerability is fixed
        Assert.IsFalse(isValid, "Vulnerability confirmed: Token for 'A' was accepted for 'AA'");
    }

    [TestMethod]
    public void TestValidateToken_InvalidPermission()
    {
        var path = "folder";
        var token = _storageService.GetToken(path, FilePermission.Upload);
        var isValid = _storageService.ValidateToken(path, token, FilePermission.Download);
        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void TestValidateToken_InvalidToken()
    {
        var isValid = _storageService.ValidateToken("folder", "invalid-token", FilePermission.Upload);
        Assert.IsFalse(isValid);
    }

    [TestMethod]
    public void TestUploadToken_CannotCrossStorageAreas()
    {
        var workspaceToken = _storageService.GetToken("avatar", FilePermission.Upload, isVault: false);
        var vaultToken = _storageService.GetToken("avatar", FilePermission.Upload, isVault: true);

        Assert.IsFalse(_storageService.ValidateToken(
            "avatar", workspaceToken, FilePermission.Upload, isVault: true));
        Assert.IsFalse(_storageService.ValidateToken(
            "avatar", vaultToken, FilePermission.Upload, isVault: false));
    }

    [TestMethod]
    public void TestDownloadToken_IsExactAndBoundToVault()
    {
        var token = _storageService.GetToken("contract/report.pdf", FilePermission.Download, isVault: true);

        Assert.IsTrue(_storageService.ValidateToken(
            "contract/report.pdf", token, FilePermission.Download, isVault: true));
        Assert.IsFalse(_storageService.ValidateToken(
            "contract/report.pdf/child", token, FilePermission.Download, isVault: true));
        Assert.IsFalse(_storageService.ValidateToken(
            "contract/report.pdf", token, FilePermission.Download, isVault: false));
    }

    [TestMethod]
    public void TestUploadGrant_CarriesServerEnforcedPolicy()
    {
        var policy = FileUploadPolicy.Create(7, "jpg png");
        var token = _storageService.GetToken(
            "avatar", FilePermission.Upload, isVault: false, uploadPolicy: policy);

        var isValid = _storageService.TryValidateToken(
            "avatar", token, FilePermission.Upload, isVault: false, out var grant);

        Assert.IsTrue(isValid);
        Assert.IsNotNull(grant.UploadPolicy);
        Assert.AreEqual(7L * 1024 * 1024, grant.UploadPolicy.MaxBytes);
        Assert.AreEqual(FileValidationKind.RasterImage, grant.UploadPolicy.ValidationKind);
        Assert.IsFalse(grant.UploadPolicy.RequireAuthenticatedUser);
        CollectionAssert.AreEquivalent(new[] { "jpg", "png" }, grant.UploadPolicy.AllowedExtensions);
    }
}
