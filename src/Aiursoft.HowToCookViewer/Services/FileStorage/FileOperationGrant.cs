namespace Aiursoft.HowToCookViewer.Services.FileStorage;

public enum FilePermission
{
    Upload,
    Download
}

public enum FileStorageArea
{
    Workspace,
    Vault
}

public sealed record FileOperationGrant(
    int Version,
    string Path,
    FilePermission Permission,
    FileStorageArea StorageArea,
    bool AllowDescendants,
    FileUploadPolicy? UploadPolicy)
{
    public const int CurrentVersion = 2;
}
