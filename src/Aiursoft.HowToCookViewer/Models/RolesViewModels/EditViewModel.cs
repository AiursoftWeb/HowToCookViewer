// ... other using statements

using System.ComponentModel.DataAnnotations;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.HowToCookViewer.Models.RolesViewModels;

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit Role";
        Claims = [];
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Id")]
    [FromRoute]
    public required string Id { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Role Name")]
    public required string RoleName { get; set; }

    [Display(Name = "Claims")]
    public List<RoleClaimViewModel> Claims { get; set; }
}
