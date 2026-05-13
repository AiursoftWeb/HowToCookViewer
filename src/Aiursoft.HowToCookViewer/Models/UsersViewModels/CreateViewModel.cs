using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Aiursoft.CSTools.Attributes;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.UsersViewModels;

public class CreateViewModel : UiStackLayoutViewModel
{
    public CreateViewModel()
    {
        PageTitle = "Create User";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "User name")]
    [ValidDomainName]
    public string? UserName { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Name")]
    [NotNull]
    [MaxLength(30, ErrorMessage = "The {0} must be at max {1} characters long.")]
    [MinLength(2, ErrorMessage = "The {0} must be at least {1} characters long.")]
    public string? DisplayName { get; set; }

    [EmailAddress(ErrorMessage = "The {0} is not a valid email address.")]
    [Display(Name = "Email Address")]
    [Required(ErrorMessage = "The {0} is required.")]
    public string? Email { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string? Password { get; set; }
}
