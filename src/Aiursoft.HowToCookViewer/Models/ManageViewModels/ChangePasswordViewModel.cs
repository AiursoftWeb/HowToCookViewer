using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.ManageViewModels;

public class ChangePasswordViewModel : UiStackLayoutViewModel
{
    public ChangePasswordViewModel()
    {
        PageTitle = "Change Password";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string? OldPassword { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.",
        MinimumLength = 6)]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string? NewPassword { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
    public string? ConfirmPassword { get; set; }
}
