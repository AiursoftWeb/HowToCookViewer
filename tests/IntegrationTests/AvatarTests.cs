using Aiursoft.HowToCookViewer.Services.FileStorage;

namespace Aiursoft.HowToCookViewer.Tests.IntegrationTests;

// JB scanner bug. Not a warning.
#pragma warning disable CS8602

[TestClass]
public class AvatarTests : TestBase
{
    [TestMethod]
    public async Task ChangeAvatarSuccessfullyTest()
    {
        // 1. Register and Login
        await RegisterAndLoginAsync();

        // 2. Upload a file
        // 1x1 transparent GIF
        var gifBytes = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
        var fileContent = new ByteArrayContent(gifBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/gif");

        var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(fileContent, "file", "avatar.gif");

        var storage = GetService<StorageService>();
        var uploadUrl = storage.GetUploadUrl("avatars", isVault: false);
        var uploadResponse = await Http.PostAsync(uploadUrl, multipartContent);
        uploadResponse.EnsureSuccessStatusCode();

        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();
        Assert.IsNotNull(uploadResult);
        Assert.IsNotNull(uploadResult.Path);

        // 3. Change Avatar
        var changeAvatarResponse = await PostForm("/Manage/ChangeAvatar", new Dictionary<string, string>
        {
            { "AvatarUrl", uploadResult.Path }
        });

        // 4. Verify Success
        AssertRedirect(changeAvatarResponse, "/Manage?Message=ChangeAvatarSuccess");
    }

    [TestMethod]
    public async Task AvatarImageProcessingTest()
    {
        // 1. Register and Login
        await RegisterAndLoginAsync();

        // 2. Upload a file
        var gifBytes = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
        var fileContent = new ByteArrayContent(gifBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/gif");

        var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(fileContent, "file", "avatar.gif");

        var storage = GetService<StorageService>();
        var uploadUrl = storage.GetUploadUrl("avatars", isVault: false);
        var uploadResponse = await Http.PostAsync(uploadUrl, multipartContent);
        uploadResponse.EnsureSuccessStatusCode();

        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();
        Assert.IsNotNull(uploadResult);
        Assert.IsNotNull(uploadResult.InternetPath);

        // 3. Test Clear EXIF (Default download)
        var downloadResponse = await Http.GetAsync(uploadResult.InternetPath);
        downloadResponse.EnsureSuccessStatusCode();
        Assert.AreEqual("image/gif", downloadResponse.Content.Headers.ContentType?.MediaType);

        // 4. Test Compression
        var compressedResponse = await Http.GetAsync(uploadResult.InternetPath + "?w=100");
        compressedResponse.EnsureSuccessStatusCode();
        Assert.AreEqual("image/gif", compressedResponse.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task AvatarPngCompressionTest()
    {
        // 1. Register and Login
        await RegisterAndLoginAsync();

        // 2. Upload a PNG file
        // 1x2 PNG
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAACCAIAAAAW4yFwAAAAEElEQVR4nGP4z8DAxMDAAAAHCQEClNBcOwAAAABJRU5ErkJggg==");
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(fileContent, "file", "avatar.png");

        var storage = GetService<StorageService>();
        var uploadUrl = storage.GetUploadUrl("avatars", isVault: false);
        var uploadResponse = await Http.PostAsync(uploadUrl, multipartContent);
        uploadResponse.EnsureSuccessStatusCode();

        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();
        Assert.IsNotNull(uploadResult);
        Assert.IsNotNull(uploadResult.InternetPath);

        // 3. Test Compression
        var compressedResponse = await Http.GetAsync(uploadResult.InternetPath + "?w=100");
        compressedResponse.EnsureSuccessStatusCode();

        // Verify it is an image and likely PNG
        Assert.AreEqual("image/png", compressedResponse.Content.Headers.ContentType?.MediaType);

        // Verify dimensions
        await using var stream = await compressedResponse.Content.ReadAsStreamAsync();
        using var image = await SixLabors.ImageSharp.Image.LoadAsync(stream);
        Assert.AreEqual(128, image.Width);
        Assert.AreEqual(256, image.Height);
    }

    [TestMethod]
    public async Task AvatarPngCompressionSquareTest()
    {
        // 1. Register and Login
        await RegisterAndLoginAsync();

        // 2. Upload a PNG file
        // 1x2 PNG
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAACCAIAAAAW4yFwAAAAEElEQVR4nGP4z8DAxMDAAAAHCQEClNBcOwAAAABJRU5ErkJggg==");
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(fileContent, "file", "avatar.png");

        var storage = GetService<StorageService>();
        var uploadUrl = storage.GetUploadUrl("avatars", isVault: false);
        var uploadResponse = await Http.PostAsync(uploadUrl, multipartContent);
        uploadResponse.EnsureSuccessStatusCode();

        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();
        Assert.IsNotNull(uploadResult);
        Assert.IsNotNull(uploadResult.InternetPath);

        // 3. Test Compression
        var compressedResponse = await Http.GetAsync(uploadResult.InternetPath + "?w=100&square=true");
        compressedResponse.EnsureSuccessStatusCode();

        // Verify it is an image and likely PNG
        Assert.AreEqual("image/png", compressedResponse.Content.Headers.ContentType?.MediaType);

        // Verify dimensions
        await using var stream = await compressedResponse.Content.ReadAsStreamAsync();
        using var image = await SixLabors.ImageSharp.Image.LoadAsync(stream);
        Assert.AreEqual(128, image.Width);
        Assert.AreEqual(128, image.Height);
    }

    [TestMethod]
    public async Task AvatarPngCompressionWidthOnlyTest()
    {
        // 1. Register and Login
        await RegisterAndLoginAsync();

        // 2. Upload a PNG file
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAACCAIAAAAW4yFwAAAAEElEQVR4nGP4z8DAxMDAAAAHCQEClNBcOwAAAABJRU5ErkJggg==");
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(fileContent, "file", "avatar.png");

        var storage = GetService<StorageService>();
        var uploadUrl = storage.GetUploadUrl("avatars", isVault: false);
        var uploadResponse = await Http.PostAsync(uploadUrl, multipartContent);
        uploadResponse.EnsureSuccessStatusCode();

        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();
        Assert.IsNotNull(uploadResult);

        // 3. Test Compression with width only
        var compressedResponse = await Http.GetAsync(uploadResult.InternetPath + "?w=100");
        compressedResponse.EnsureSuccessStatusCode();

        await using var stream = await compressedResponse.Content.ReadAsStreamAsync();
        using var image = await SixLabors.ImageSharp.Image.LoadAsync(stream);
        Assert.AreEqual(128, image.Width);
    }

    [TestMethod]
    public async Task TestClearExif()
    {
        await RegisterAndLoginAsync();

        // Upload a PNG file
        var pngBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAACCAIAAAAW4yFwAAAAEElEQVR4nGP4z8DAxMDAAAAHCQEClNBcOwAAAABJRU5ErkJggg==");
        var fileContent = new ByteArrayContent(pngBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");

        var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(fileContent, "file", "avatar.png");

        var storage = GetService<StorageService>();
        var uploadUrl = storage.GetUploadUrl("avatars", isVault: false);
        var uploadResponse = await Http.PostAsync(uploadUrl, multipartContent);
        uploadResponse.EnsureSuccessStatusCode();

        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();
        Assert.IsNotNull(uploadResult);

        // Download without any parameters should trigger ClearExif
        var downloadResponse = await Http.GetAsync(uploadResult.InternetPath);
        downloadResponse.EnsureSuccessStatusCode();
        Assert.AreEqual("image/png", downloadResponse.Content.Headers.ContentType?.MediaType);
    }

    [TestMethod]
    public async Task TestProcessNonImage()
    {
        await RegisterAndLoginAsync();

        // Upload a text file but give it an image extension to try to trick the processor
        var content = new StringContent("Not an image");
        var multipartContent = new MultipartFormDataContent();
        multipartContent.Add(content, "file", "fake.jpg");

        var storage = GetService<StorageService>();
        var uploadUrl = storage.GetUploadUrl("test", isVault: false);
        var uploadResponse = await Http.PostAsync(uploadUrl, multipartContent);
        uploadResponse.EnsureSuccessStatusCode();

        var uploadResult = await uploadResponse.Content.ReadFromJsonAsync<UploadResult>();
        Assert.IsNotNull(uploadResult);

        // Try to compress it. FilesController.Download will call physicalPath.IsStaticImage() 
        // which might return true based on extension, but Image.LoadAsync will fail.
        var compressedResponse = await Http.GetAsync(uploadResult.InternetPath + "?w=100");
        
        // It should still return the file (original) or successfully handle the error.
        compressedResponse.EnsureSuccessStatusCode();
    }

    [TestMethod]
    public async Task TestIsValidImageWithNonExistingFile()
    {
        var service = GetService<ImageProcessingService>();
        var result = await service.IsValidImageAsync("/non/existing/path.jpg");
        Assert.IsFalse(result);
    }

    private class UploadResult
    {
        public string Path { get; init; } = string.Empty;
        public string InternetPath { get; init; } = string.Empty;
    }
}
#pragma warning restore CS8602
#pragma warning restore CS8602
