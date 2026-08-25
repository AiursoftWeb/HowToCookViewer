namespace Aiursoft.HowToCookViewer.Services.FileStorage;

public enum FileValidationKind
{
    ExtensionOnly,
    RasterImage
}

public sealed record FileUploadPolicy(
    long MaxBytes,
    string[] AllowedExtensions,
    FileValidationKind ValidationKind,
    bool RequireAuthenticatedUser,
    bool ReplaceSpacesWithHyphens)
{
    public const int DefaultMaxSizeInMb = 10;
    public const int AbsoluteMaxSizeInMb = 2048;

    private static readonly HashSet<string> RasterImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "bmp", "gif", "jpeg", "jpg", "png", "webp"
    };

    public static FileUploadPolicy Create(
        int maxSizeInMb,
        string? allowedExtensions,
        bool requireAuthenticatedUser = false,
        bool replaceSpacesWithHyphens = false)
    {
        if (maxSizeInMb <= 0 || maxSizeInMb > AbsoluteMaxSizeInMb)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxSizeInMb),
                $"Upload size must be between 1 and {AbsoluteMaxSizeInMb} MB.");
        }

        var extensions = string.IsNullOrWhiteSpace(allowedExtensions)
            ? ["*"]
            : allowedExtensions
                .Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(NormalizeExtension)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (extensions.Length == 0)
        {
            throw new ArgumentException("At least one allowed extension is required.", nameof(allowedExtensions));
        }

        var validationKind = extensions.All(RasterImageExtensions.Contains)
            ? FileValidationKind.RasterImage
            : FileValidationKind.ExtensionOnly;

        return new FileUploadPolicy(
            MaxBytes: checked((long)maxSizeInMb * 1024 * 1024),
            AllowedExtensions: extensions,
            ValidationKind: validationKind,
            RequireAuthenticatedUser: requireAuthenticatedUser,
            ReplaceSpacesWithHyphens: replaceSpacesWithHyphens);
    }

    public bool IsExtensionAllowed(string fileName)
    {
        if (AllowedExtensions.Contains("*", StringComparer.Ordinal))
        {
            return true;
        }

        var extension = NormalizeExtension(Path.GetExtension(fileName));
        return extension.Length > 0 && AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    public bool IsStructurallyValid()
    {
        return MaxBytes > 0 &&
               MaxBytes <= (long)AbsoluteMaxSizeInMb * 1024 * 1024 &&
               AllowedExtensions is { Length: > 0 } &&
               AllowedExtensions.All(extension =>
                   extension == "*" ||
                   (extension.Length > 0 && extension.All(character => char.IsLetterOrDigit(character)))) &&
               Enum.IsDefined(ValidationKind) &&
               (ValidationKind != FileValidationKind.RasterImage ||
                AllowedExtensions.All(RasterImageExtensions.Contains));
    }

    private static string NormalizeExtension(string extension)
    {
        return extension.Trim().TrimStart('.').ToLowerInvariant();
    }
}
