using System.Net;
using Aiursoft.HowToCookViewer.Services.FileStorage;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

[TestClass]
public class FilesControllerTests : TestBase
{
    [TestMethod]
    public async Task TestUploadAndDownload()
    {
        // 1. Upload
        var storage = GetService<StorageService>();
        var uploadUrl = storage.GetUploadUrl("test", isVault: false);

        var content = new StringContent("Hello World");
        var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(content, "file", "test.txt");

        var uploadResponse = await Http.PostAsync(uploadUrl, multipartContent);
        uploadResponse.EnsureSuccessStatusCode();
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();
        Assert.IsNotNull(uploadResult);
        Assert.IsNotNull(uploadResult.Path);

        // 2. Download
        var downloadResponse = await Http.GetAsync("/download/" + uploadResult.Path);
        downloadResponse.EnsureSuccessStatusCode();
        var downloadContent = await downloadResponse.Content.ReadAsStringAsync();
        Assert.AreEqual("Hello World", downloadContent);
    }

    [TestMethod]
    public async Task TestPrivateUploadAndDownload()
    {
        var storage = GetService<StorageService>();
        var subfolder = "private-test";
        var uploadUrl = storage.GetUploadUrl(subfolder, isVault: true);

        // 1. Upload
        var content = new StringContent("Private Hello World");
        var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(content, "file", "private-test.txt");

        var uploadResponse = await Http.PostAsync(uploadUrl, multipartContent);
        uploadResponse.EnsureSuccessStatusCode();
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();
        Assert.IsNotNull(uploadResult);
        Assert.IsNotNull(uploadResult.Path);
        Assert.IsNotNull(uploadResult.InternetPath);
        Assert.Contains("token=", uploadResult.InternetPath);

        // 2. Download using InternetPath (which contains token)
        var downloadResponse = await Http.GetAsync(uploadResult.InternetPath);
        downloadResponse.EnsureSuccessStatusCode();
        var downloadContent = await downloadResponse.Content.ReadAsStringAsync();
        Assert.AreEqual("Private Hello World", downloadContent);

        // 3. Try download without token
        var rawPath = uploadResult.Path;
        var unauthorizedResponse = await Http.GetAsync("/download-private/" + rawPath);
        Assert.AreEqual(HttpStatusCode.Unauthorized, unauthorizedResponse.StatusCode);

        // 4. Try download with invalid token
        var invalidTokenResponse = await Http.GetAsync("/download-private/" + rawPath + "?token=invalid");
        Assert.AreEqual(HttpStatusCode.Unauthorized, invalidTokenResponse.StatusCode);
    }

    [TestMethod]
    public async Task TestPrivateUploadWithInvalidToken()
    {
        var subfolder = "private-test";
        var uploadResponse = await Http.PostAsync($"/upload-private/{subfolder}?token=invalid", new MultipartFormDataContent());
        Assert.AreEqual(HttpStatusCode.Unauthorized, uploadResponse.StatusCode);
    }

    [TestMethod]
    public async Task TestUploadInvalidFileName()
    {
        var storage = GetService<StorageService>();
        var uploadUrl = storage.GetUploadUrl("test", isVault: false);

        var content = new StringContent("Hello World");
        var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(content, "file", "../test.txt");

        var uploadResponse = await Http.PostAsync(uploadUrl, multipartContent);
        Assert.AreEqual(HttpStatusCode.BadRequest, uploadResponse.StatusCode);
    }

    [TestMethod]
    public async Task TestDownloadNotFound()
    {
        var downloadResponse = await Http.GetAsync("/download/non-existing.txt");
        Assert.AreEqual(HttpStatusCode.NotFound, downloadResponse.StatusCode);
    }

    [TestMethod]
    public async Task TestPrivateUploadPathTraversal()
    {
        var storage = GetService<StorageService>();
        var subfolder = "folderA";
        // Get token for folderA
        var token = storage.GetToken(subfolder, FilePermission.Upload);

        // Attempt to upload to folderA/../folderB
        // We use double encoded slashes or explicitly encoded content to ensure it reaches the controller as "folderA/../folderB"
        // If we just use "folderA/../folderB", the HTTP client or server might normalize it to "folderB" before it hits our logic.
        // We want to test that IF the controller receives "folderA/../folderB", our logic rejects it.
        var maliciousPath = "folderA%2F..%2FfolderB";
        
        var content = new StringContent("Malicious Content");
        var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(content, "file", "hack.txt");

        var uploadResponse = await Http.PostAsync($"/upload-private/{maliciousPath}?token={token}", multipartContent);
        
        // Should be rejected because the path contains ".."
        Assert.AreEqual(HttpStatusCode.Unauthorized, uploadResponse.StatusCode);
    }

    [TestMethod]
    public async Task TestDownloadWithETag()
    {
        // 1. Upload
        var storage = GetService<StorageService>();
        var uploadUrl = storage.GetUploadUrl("test", isVault: false);

        var content = new StringContent("ETag Test Content");
        var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(content, "file", "etag.txt");

        var uploadResponse = await Http.PostAsync(uploadUrl, multipartContent);
        uploadResponse.EnsureSuccessStatusCode();
        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();
        Assert.IsNotNull(uploadResult);

        // 2. First download to get ETag
        var downloadResponse = await Http.GetAsync("/download/" + uploadResult.Path);
        downloadResponse.EnsureSuccessStatusCode();
        var etag = downloadResponse.Headers.ETag?.Tag ?? downloadResponse.Headers.GetValues("ETag").FirstOrDefault();
        Assert.IsNotNull(etag);

        // 3. Second download with If-None-Match
        var request = new HttpRequestMessage(HttpMethod.Get, "/download/" + uploadResult.Path);
        request.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var response304 = await Http.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.NotModified, response304.StatusCode);
    }

    private class UploadResult
    {
        public string Path { get; init; } = string.Empty;
        public string InternetPath { get; init; } = string.Empty;
    }
}
