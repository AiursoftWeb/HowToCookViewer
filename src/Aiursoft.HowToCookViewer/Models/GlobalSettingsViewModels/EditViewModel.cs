using System.ComponentModel.DataAnnotations;

namespace Aiursoft.HowToCookViewer.Models.GlobalSettingsViewModels;

public class EditViewModel
{
    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Key")]
    public string Key { get; set; } = string.Empty;

    [Display(Name = "Value")]
    public string? Value { get; set; }
}
