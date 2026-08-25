using Aiursoft.Scanner.Abstractions;
using System.Xml;

namespace Aiursoft.HowToCookViewer.Services.FileStorage;

public class FileDeliveryPolicy(IConfiguration configuration) : ISingletonDependency
{
    private static readonly IReadOnlyDictionary<string, string> SupportedMediaTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mp3"] = "audio/mpeg",
            ["mp4"] = "video/mp4",
            ["ogg"] = "audio/ogg",
            ["svg"] = "image/svg+xml",
            ["wav"] = "audio/wav",
            ["webm"] = "video/webm"
        };

    private readonly HashSet<string> _allowedInlineMediaExtensions = GetConfiguredExtensions(configuration);
    private readonly string[] _safeInlineSvgSubfolders = GetConfiguredSvgSubfolders(configuration);

    public bool CanRenderInline(HttpRequest request)
    {
        if (!configuration.GetValue<bool>("Storage:RequireDedicatedInlineOrigin"))
        {
            return true;
        }

        var publicOrigin = StorageService.GetPublicOrigin(configuration);
        return publicOrigin is not null &&
               string.Equals(request.Scheme, publicOrigin.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(request.Host.Value, publicOrigin.Authority, StringComparison.OrdinalIgnoreCase);
    }

    public bool TryGetVerifiedInlineMediaType(
        string logicalPath,
        string physicalPath,
        out string contentType)
    {
        contentType = string.Empty;
        var extension = Path.GetExtension(physicalPath).TrimStart('.').ToLowerInvariant();
        if (!_allowedInlineMediaExtensions.Contains(extension) ||
            !SupportedMediaTypes.TryGetValue(extension, out var configuredContentType))
        {
            return false;
        }

        if (extension == "svg")
        {
            if (IsAllowedSvgPath(logicalPath) && IsValidSvg(physicalPath))
            {
                contentType = configuredContentType;
                return true;
            }

            return false;
        }

        Span<byte> header = stackalloc byte[16];
        using var stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytesRead = stream.Read(header);
        var isValid = extension switch
        {
            "mp3" => IsMp3(header[..bytesRead]),
            "mp4" => bytesRead >= 12 && header[4..8].SequenceEqual("ftyp"u8),
            "ogg" => bytesRead >= 4 && header[..4].SequenceEqual("OggS"u8),
            "wav" => bytesRead >= 12 &&
                     header[..4].SequenceEqual("RIFF"u8) &&
                     header[8..12].SequenceEqual("WAVE"u8),
            "webm" => bytesRead >= 4 &&
                      header[0] == 0x1A &&
                      header[1] == 0x45 &&
                      header[2] == 0xDF &&
                      header[3] == 0xA3,
            _ => false
        };

        if (isValid)
        {
            contentType = configuredContentType;
        }

        return isValid;
    }

    private bool IsAllowedSvgPath(string logicalPath)
    {
        var normalizedPath = logicalPath.Replace('\\', '/').Trim('/');
        return _safeInlineSvgSubfolders.Any(subfolder =>
            normalizedPath.StartsWith(subfolder + "/", StringComparison.Ordinal));
    }

    private static bool IsValidSvg(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = FileUploadPolicy.DefaultMaxSizeInMb * 1024L * 1024L
            });

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    return reader.LocalName == "svg" &&
                           reader.NamespaceURI == "http://www.w3.org/2000/svg";
                }
            }
        }
        catch (XmlException)
        {
            return false;
        }

        return false;
    }

    private static HashSet<string> GetConfiguredExtensions(IConfiguration configuration)
    {
        var values = configuration
            .GetSection("Storage:SafeInlineMediaExtensions")
            .Get<string[]>() ?? [];
        return values
            .Select(value => value.Trim().TrimStart('.').ToLowerInvariant())
            .Where(SupportedMediaTypes.ContainsKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string[] GetConfiguredSvgSubfolders(IConfiguration configuration)
    {
        var values = configuration
            .GetSection("Storage:SafeInlineSvgSubfolders")
            .Get<string[]>() ?? [];
        return values
            .Select(value => value.Trim().Trim('/', '\\').Replace('\\', '/'))
            .Where(value => value.Length > 0 &&
                            value.Split('/').All(segment => segment is not "" and not "." and not ".."))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsMp3(ReadOnlySpan<byte> header)
    {
        return header.Length >= 3 && header[..3].SequenceEqual("ID3"u8) ||
               header.Length >= 2 && header[0] == 0xFF && (header[1] & 0xE0) == 0xE0;
    }
}
