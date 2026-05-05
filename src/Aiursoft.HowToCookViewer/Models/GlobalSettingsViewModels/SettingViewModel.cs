using System.ComponentModel.DataAnnotations;

namespace Aiursoft.HowToCookViewer.Models.GlobalSettingsViewModels;

public class SettingViewModel
{
    [Display(Name = "Key")]
    public required string Key { get; set; }

    [Display(Name = "Name")]
    public required string Name { get; set; }

    [Display(Name = "Description")]
    public required string Description { get; set; }

    [Display(Name = "Type")]
    public required SettingType Type { get; set; }

    [Display(Name = "Value")]
    public string? Value { get; set; }

    [Display(Name = "Default value")]
    public required string DefaultValue { get; set; }

    [Display(Name = "Is overridden by config")]
    public bool IsOverriddenByConfig { get; set; }

    [Display(Name = "Choice options")]
    public Dictionary<string, string>? ChoiceOptions { get; set; }
    
    // File upload settings (for SettingType.File)
    [Display(Name = "Subfolder")]
    public string? Subfolder { get; set; }

    [Display(Name = "Allowed extensions")]
    public string? AllowedExtensions { get; set; }

    [Display(Name = "Max size in MB")]
    public int MaxSizeInMb { get; set; }
}
