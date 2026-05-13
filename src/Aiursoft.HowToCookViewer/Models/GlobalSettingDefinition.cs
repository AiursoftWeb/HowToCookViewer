using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.HowToCookViewer.Models;

[ExcludeFromCodeCoverage]
public class GlobalSettingDefinition
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required SettingType Type { get; init; }
    public required string DefaultValue { get; init; }
    public Dictionary<string, string>? ChoiceOptions { get; init; }

    // File upload settings (for SettingType.File)
    public string? Subfolder { get; init; }
    public string? AllowedExtensions { get; init; }
    public int MaxSizeInMb { get; init; } = 10;
}
