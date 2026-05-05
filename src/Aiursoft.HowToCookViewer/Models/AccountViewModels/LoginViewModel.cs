using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.AccountViewModels;

public class LoginViewModel: UiStackLayoutViewModel
{
    public LoginViewModel()
    {
        PageTitle = "Login";
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name ="Email or User name")]
    public string? EmailOrUserName { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [DataType(DataType.Password)]
    [Display(Name ="Password")]
    [StringLength(100, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
    public string? Password { get; set; }
}
