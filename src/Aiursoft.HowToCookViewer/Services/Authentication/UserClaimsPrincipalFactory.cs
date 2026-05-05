// In Services/CustomClaimsPrincipalFactory.cs

using System.Security.Claims;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
// Your User class namespace

namespace Aiursoft.HowToCookViewer.Services.Authentication;

/// <summary>
/// This class extends the default UserClaimsPrincipalFactory to include additional claims.
///
/// Because this application uses cookies to persist user sessions, and all user information is stored in the cookie,
/// We need to add custom claims here so that they are available throughout the application.
///
/// For example, we may need to show the avatar of the user in the navigation bar, so we add the avatar claim here.
/// If you didn't put it here, you would have to query the database every time you want to show the avatar, and could not
/// use the cached information in the cookie.
/// </summary>
/// <param name="roleManager"></param>
/// <param name="userManager"></param>
/// <param name="optionsAccessor"></param>
public class UserClaimsPrincipalFactory(
    RoleManager<IdentityRole> roleManager,
    UserManager<User> userManager,
    IOptions<IdentityOptions> optionsAccessor)
    : UserClaimsPrincipalFactory<User, IdentityRole>(userManager, roleManager, optionsAccessor)
{
    /// <summary>
    /// This is the claim type for the display name of the user.
    ///
    /// To get the display name of the user, use:
   ///  HttpContext.User.Claims.First(c => c.Type == UserClaimsPrincipalFactory.DisplayNameClaimType).Value
    /// </summary>
    public static string DisplayNameClaimType = "DisplayName";

    /// <summary>
    /// This is the claim type for the avatar of the user.
    ///
    /// To get the avatar of the user, use:
    /// HttpContext.User.Claims.First(c => c.Type == UserClaimsPrincipalFactory.AvatarClaimType).Value
    /// </summary>
    public static string AvatarClaimType = "Avatar";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(User user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        identity.AddClaim(new Claim(DisplayNameClaimType, user.DisplayName));
        identity.AddClaim(new Claim(AvatarClaimType, user.AvatarRelativePath));
        return identity;
    }
}
