using System.ComponentModel.DataAnnotations;

namespace Aiursoft.HowToCookViewer.Models.PermissionsViewModels;

public class PermissionWithRoleCount
{
    [Display(Name = "Permission Key")]
    public required string PermissionKey { get; init; }

    [Display(Name = "Permission Name")]
    public required string PermissionName { get; init; }

    [Display(Name = "Permission Description")]
    public required string PermissionDescription { get; init; }

    [Display(Name = "Role Count")]
    public required int RoleCount { get; init; }

    [Display(Name = "User Count")]
    public required int UserCount { get; init; }
}
