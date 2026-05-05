using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Aiursoft.CSTools.Attributes;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.HowToCookViewer.Models.UsersViewModels;

// Manage if a role is selected or not in the UI.

public class EditViewModel : UiStackLayoutViewModel
{
    public EditViewModel()
    {
        PageTitle = "Edit User";
        AllRoles = [];
    }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "User name")]
    [ValidDomainName]
    public required string UserName { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Name")]
    [MaxLength(30, ErrorMessage = "The {0} must be at max {1} characters long.")]
    [MinLength(2, ErrorMessage = "The {0} must be at least {1} characters long.")]
    public required string DisplayName { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [EmailAddress(ErrorMessage = "The {0} is not a valid email address.")]
    [Display(Name = "Email Address")]
    public required string Email { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "Reset Password (leave empty to keep the same password)")]
    public string? Password { get; set; }

    [NotNull]
    [Display(Name = "Avatar file")]
    [Required(ErrorMessage = "The avatar file is required.")]
    [RegularExpression(@"^avatar.*", ErrorMessage = "The avatar file is invalid. Please upload it again.")]
    [MaxLength(150, ErrorMessage = "The {0} must be at max {1} characters long.")]
    [MinLength(2, ErrorMessage = "The {0} must be at least {1} characters long.")]
    public string? AvatarUrl { get; set; }

    [Required(ErrorMessage = "The {0} is required.")]
    [Display(Name = "Id")]
    [FromRoute]
    public required string Id { get; set; }

    [Display(Name = "All roles")]
    public List<UserRoleViewModel> AllRoles { get; set; }
}
