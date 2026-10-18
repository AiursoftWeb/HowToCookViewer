using System.ComponentModel.DataAnnotations;

namespace Aiursoft.HowToCookViewer.Models.GlobalSettingsViewModels;

public class SettingViewModel
{

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Key")]
    public required string Key { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Name")]
    public required string Name { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Description")]
    public required string Description { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Type")]
    public required SettingType Type { get; set; }

    [Display(Name = "Value")]
    public string? Value { get; set; }

    [Display(Name = "Is Configured")]
    public bool IsConfigured { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Default Value")]
    public required string DefaultValue { get; set; }

    [Display(Name = "Is Overridden By Config")]
    public bool IsOverriddenByConfig { get; set; }

    [Display(Name = "Choice Options")]
    public Dictionary<string, string>? ChoiceOptions { get; set; }

    // File upload settings (for SettingType.File)
    [Display(Name = "Subfolder")]
    public string? Subfolder { get; set; }

    [Display(Name = "Allowed Extensions")]
    public string? AllowedExtensions { get; set; }

    [Display(Name = "Max Size (MB)")]
    public int MaxSizeInMb { get; set; }
}
