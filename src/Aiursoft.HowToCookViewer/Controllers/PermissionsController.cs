using Aiursoft.HowToCookViewer.Authorization;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models.PermissionsViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Controllers;

/// <summary>
/// This controller is used to display permissions and their assignments to roles and users.
/// Permissions are read-only and cannot be edited or deleted.
/// </summary>
[Authorize]
[LimitPerMin]
public class PermissionsController(
    RoleManager<IdentityRole> roleManager,
    TemplateDbContext context)
    : Controller
{
    [Authorize(Policy = AppPermissionNames.CanReadPermissions)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Directory",
        CascadedLinksIcon = "users",
        CascadedLinksOrder = 9998,
        LinkText = "Permissions",
        LinkOrder = 3)]
    public async Task<IActionResult> Index()
    {
        var allPermissions = AppPermissions.GetAllPermissions();

        // Get all role claims that are permission-type claims
        var allRoleClaims = await context.RoleClaims
            .Where(rc => rc.ClaimType == AppPermissions.Type)
            .ToListAsync();

        // Group by claim value (permission key) and count roles
        var roleCountByPermission = allRoleClaims
            .GroupBy(rc => rc.ClaimValue)
            .ToDictionary(g => g.Key!, g => g.Select(rc => rc.RoleId).Distinct().Count());

        // Calculate user count for each permission
        var userCountByPermission = new Dictionary<string, int>();
        foreach (var permission in allPermissions)
        {
            // Get all role IDs that have this permission
            var roleIdsWithPermission = allRoleClaims
                .Where(rc => rc.ClaimValue == permission.Key)
                .Select(rc => rc.RoleId)
                .Distinct()
                .ToList();

            // Count distinct users who have any of these roles
            if (roleIdsWithPermission.Any())
            {
                var userCount = await context.UserRoles
                    .Where(ur => roleIdsWithPermission.Contains(ur.RoleId))
                    .Select(ur => ur.UserId)
                    .Distinct()
                    .CountAsync();
                userCountByPermission[permission.Key] = userCount;
            }
            else
            {
                userCountByPermission[permission.Key] = 0;
            }
        }

        var permissionsWithCounts = allPermissions.Select(permission => new PermissionWithRoleCount
        {
            PermissionKey = permission.Key,
            PermissionName = permission.Name,
            PermissionDescription = permission.Description,
            RoleCount = roleCountByPermission.GetValueOrDefault(permission.Key, 0),
            UserCount = userCountByPermission.GetValueOrDefault(permission.Key, 0)
        }).ToList();

        return this.StackView(new IndexViewModel
        {
            Permissions = permissionsWithCounts
        });
    }

    [Authorize(Policy = AppPermissionNames.CanReadPermissions)]
    public async Task<IActionResult> Details(string? key)
    {
        if (key == null) return NotFound();

        var permission = AppPermissions.GetAllPermissions()
            .FirstOrDefault(p => p.Key == key);

        if (permission == null) return NotFound();

        // Get all roles that have this permission
        var roleIdsWithPermission = await context.RoleClaims
            .Where(rc => rc.ClaimType == AppPermissions.Type && rc.ClaimValue == key)
            .Select(rc => rc.RoleId)
            .Distinct()
            .ToListAsync();

        var roles = await roleManager.Roles
            .Where(r => roleIdsWithPermission.Contains(r.Id))
            .ToListAsync();

        // Get all users who have any of these roles
        var userIdsWithPermission = await context.UserRoles
            .Where(ur => roleIdsWithPermission.Contains(ur.RoleId))
            .Select(ur => ur.UserId)
            .Distinct()
            .ToListAsync();

        var users = await context.Users
            .Where(u => userIdsWithPermission.Contains(u.Id))
            .ToListAsync();

        return this.StackView(new DetailsViewModel
        {
            Permission = permission,
            Roles = roles,
            Users = users
        });
    }
}
