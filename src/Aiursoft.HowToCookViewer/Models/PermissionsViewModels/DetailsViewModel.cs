using System.ComponentModel.DataAnnotations;
using Aiursoft.HowToCookViewer.Authorization;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.HowToCookViewer.Models.PermissionsViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "Permission Details";
    }

    [Display(Name = "Permission")]
    public required PermissionDescriptor Permission { get; set; }

    [Display(Name = "Roles")]
    public required List<IdentityRole> Roles { get; set; }

    [Display(Name = "Users")]
    public required List<User> Users { get; set; }
}
