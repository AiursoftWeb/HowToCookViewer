using System.ComponentModel.DataAnnotations;

namespace Aiursoft.HowToCookViewer.Models.RolesViewModels;

public class IdentityRoleWithCount
{
    [Display(Name = "Role Id")]
    public required string RoleId { get; init; }

    [Display(Name = "Role Name")]
    public required string RoleName { get; init; }

    [Display(Name = "User Count")]
    public required int UserCount { get; init; }

    [Display(Name = "Permission Count")]
    public required int PermissionCount { get; init; }

    [Display(Name = "Permission Names")]
    public required List<string> PermissionNames { get; init; }
}
