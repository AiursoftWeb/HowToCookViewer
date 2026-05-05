using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using Aiursoft.UiStack.Layout;

namespace Aiursoft.HowToCookViewer.Models.ManageViewModels;

public class ChangeAvatarViewModel : UiStackLayoutViewModel
{
    public ChangeAvatarViewModel()
    {
        PageTitle = "Change Avatar";
    }

    [NotNull]
    [Display(Name = "Avatar file")]
    [Required(ErrorMessage = "The avatar file is required.")]
    [RegularExpression(@"^avatar.*", ErrorMessage = "The avatar file is invalid. Please upload it again.")]
    [MaxLength(150, ErrorMessage = "The {0} must be at max {1} characters long.")]
    [MinLength(2, ErrorMessage = "The {0} must be at least {1} characters long.")]
    public string? AvatarUrl { get; set; }
}
