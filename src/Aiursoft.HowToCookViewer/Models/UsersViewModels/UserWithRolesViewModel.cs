using System.ComponentModel.DataAnnotations;

namespace Aiursoft.HowToCookViewer.Models.UsersViewModels;

public class UserWithRolesViewModel
{
    [Display(Name = "Id")]
    public required string Id { get; init; }

    [Display(Name = "User name")]
    public required string UserName { get; init; }

    [Display(Name = "Name")]
    public required string DisplayName { get; init; }

    [Display(Name = "Email")]
    public required string Email { get; init; }

    [Display(Name = "Avatar relative path")]
    public required string AvatarRelativePath { get; init; }

    [Display(Name = "Roles")]
    public required IList<string> Roles { get; init; }
}
