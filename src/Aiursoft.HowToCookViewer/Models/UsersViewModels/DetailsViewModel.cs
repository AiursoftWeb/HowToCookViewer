using System.ComponentModel.DataAnnotations;
using Aiursoft.HowToCookViewer.Authorization;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.UiStack.Layout;
using Microsoft.AspNetCore.Identity;

namespace Aiursoft.HowToCookViewer.Models.UsersViewModels;

public class DetailsViewModel : UiStackLayoutViewModel
{
    public DetailsViewModel()
    {
        PageTitle = "User Details";
    }

    [Display(Name = "User")]
    public required User User { get; set; }

    [Display(Name = "Roles")]
    public required IList<IdentityRole> Roles { get; set; }

    [Display(Name = "Permissions")]
    public required List<PermissionDescriptor> Permissions { get; set; }

    [Display(Name = "Like History")]
    public List<LikeHistoryItem> Likes { get; set; } = [];

    [Display(Name = "Comment History")]
    public List<CommentHistoryItem> Comments { get; set; } = [];

    [Display(Name = "Favorite History")]
    public List<FavoriteHistoryItem> Favorites { get; set; } = [];
}

public class LikeHistoryItem
{
    public required string RecipeName { get; set; }
    public int RecipeId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CommentHistoryItem
{
    public int CommentId { get; set; }
    public required string Content { get; set; }
    public required string RecipeName { get; set; }
    public int RecipeId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class FavoriteHistoryItem
{
    public required string RecipeName { get; set; }
    public int RecipeId { get; set; }
    public DateTime CreatedAt { get; set; }
}
