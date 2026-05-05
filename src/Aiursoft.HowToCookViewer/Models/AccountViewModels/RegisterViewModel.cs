using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.AccountViewModels;

public class RegisterViewModel: UiStackLayoutViewModel
{
    public RegisterViewModel()
    {
        PageTitle = "Register";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [EmailAddress(ErrorMessage = "The {0} is not a valid email address.")]
    [Display(Name = "Email Address (For login)")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string? Password { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
    public string? ConfirmPassword { get; set; }
}
