using System.Security.Claims;
using Aiursoft.HowToCookViewer.Authorization;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models.RolesViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aiursoft.HowToCookViewer.Controllers;

/// <summary>
/// This controller is used to handle roles related actions like create, edit, delete, etc.
/// </summary>
[Authorize]
[LimitPerMin]
public class RolesController(
    UserManager<User> userManager,
    TemplateDbContext context,
    RoleManager<IdentityRole> roleManager)
    : Controller
{
    // GET: Roles
    [Authorize(Policy = AppPermissionNames.CanReadRoles)]
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "Directory",
        CascadedLinksIcon = "users",
        CascadedLinksOrder = 9998,
        LinkText = "Roles",
        LinkOrder = 2)]
    public async Task<IActionResult> Index()
    {
        var roleUserCounts = await context.UserRoles
            .GroupBy(userRole => userRole.RoleId)
            .Select(group => new
            {
                RoleId = group.Key,
                Count = group.Count()
            })
            .ToDictionaryAsync(x => x.RoleId, x => x.Count);

        var allRolePermissions = await context.RoleClaims
            .Where(rc => rc.ClaimType == AppPermissions.Type)
            .ToListAsync();

        var rolePermissionsGrouped = allRolePermissions
            .GroupBy(rc => rc.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(rc => rc.ClaimValue).ToList());

        var allPermissionsDescriptors = AppPermissions.GetAllPermissions();

        var allRoles = await roleManager.Roles.ToListAsync();
        var rolesWithCount = allRoles.Select(role =>
        {
            var permissionKeys = rolePermissionsGrouped.GetValueOrDefault(role.Id, []);
            var matchedPermissions = allPermissionsDescriptors
                .Where(p => permissionKeys.Contains(p.Key))
                .ToList();

            return new IdentityRoleWithCount
            {
                RoleId = role.Id,
                RoleName = role.Name!,
                UserCount = roleUserCounts.GetValueOrDefault(role.Id, 0),
                PermissionCount = matchedPermissions.Count,
                PermissionNames = matchedPermissions.Select(p => p.Name).ToList()
            };
        }).ToList();

        return this.StackView(new IndexViewModel
        {
            Roles = rolesWithCount
        });
    }

    // GET: Roles/Details/5
    [Authorize(Policy = AppPermissionNames.CanReadRoles)]
    public async Task<IActionResult> Details(string? id)
    {
        if (id == null) return NotFound();
        var role = await roleManager.FindByIdAsync(id);
        if (role == null) return NotFound();

        var claims = await roleManager.GetClaimsAsync(role);
        var claimValues = claims
            .Where(c => c.Type == AppPermissions.Type)
            .Select(c => c.Value)
            .ToList();

        var permissions = AppPermissions.GetAllPermissions()
            .Where(p => claimValues.Contains(p.Key))
            .ToList();

        var usersInRole = await userManager.GetUsersInRoleAsync(role.Name!);

        return this.StackView(new DetailsViewModel
        {
            Role = role,
            Permissions = permissions,
            UsersInRole = usersInRole
        });
    }

    // GET: Roles/Create
    [Authorize(Policy = AppPermissionNames.CanAddRoles)]
    public IActionResult Create()
    {
        return this.StackView(new CreateViewModel());
    }

    // POST: Roles/Create
    [HttpPost]
    [Authorize(Policy = AppPermissionNames.CanAddRoles)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateViewModel model)
    {
        if (ModelState.IsValid)
        {
            var role = new IdentityRole(model.RoleName!);
            var result = await roleManager.CreateAsync(role);
            if (result.Succeeded)
            {
                return RedirectToAction(nameof(Details), new { id = role.Id });
            }
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
        }
        return this.StackView(model);
    }

    // GET: Roles/Edit/5
    [Authorize(Policy = AppPermissionNames.CanEditRoles)]
    public async Task<IActionResult> Edit(string? id)
    {
        if (id == null) return NotFound();
        var role = await roleManager.FindByIdAsync(id);
        if (role == null) return NotFound();

        var model = new EditViewModel
        {
            Id = role.Id,
            RoleName = role.Name!
        };

        var existingClaims = await roleManager.GetClaimsAsync(role);

        foreach (var permission in AppPermissions.GetAllPermissions())
        {
            model.Claims.Add(new RoleClaimViewModel
            {
                Key = permission.Key,
                Name = permission.Name,
                Description = permission.Description,
                IsSelected = existingClaims.Any(c => c.Type == AppPermissions.Type && c.Value == permission.Key)
            });
        }

        return this.StackView(model);
    }

    // POST: Roles/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanEditRoles)]
    public async Task<IActionResult> Edit(EditViewModel model)
    {
        if (ModelState.IsValid)
        {
            var role = await roleManager.FindByIdAsync(model.Id);
            if (role == null) return NotFound();

            role.Name = model.RoleName;
            var updateResult = await roleManager.UpdateAsync(role);
            if (updateResult != IdentityResult.Success)
            {
                foreach (var error in updateResult.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return this.StackView(model);
            }

            var existingClaims = await roleManager.GetClaimsAsync(role);

            // Remove unselected claims
            foreach (var existingClaim in existingClaims)
            {
                // Compare against the Key
                if (!model.Claims.Any(c => c.Key == existingClaim.Value && c.IsSelected))
                {
                    await roleManager.RemoveClaimAsync(role, existingClaim);
                }
            }

            // Add newly selected claims
            foreach (var claimViewModel in model.Claims)
            {
                // Check against the Key
                if (claimViewModel.IsSelected && existingClaims.All(c => c.Value != claimViewModel.Key))
                {
                    // Add the claim using the Key
                    await roleManager.AddClaimAsync(role, new Claim(AppPermissions.Type, claimViewModel.Key));
                }
            }

            return RedirectToAction(nameof(Details), new { id = role.Id });
        }
        return this.StackView(model);
    }

    // GET: Roles/Delete/5
    [Authorize(Policy = AppPermissionNames.CanDeleteRoles)]
    public async Task<IActionResult> Delete(string? id)
    {
        if (id == null)
        {
            return NotFound();
        }

        var role = await roleManager.FindByIdAsync(id);
        if (role == null)
        {
            return NotFound();
        }

        return this.StackView(new DeleteViewModel
        {
            Role = role
        });
    }

    // POST: Roles/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = AppPermissionNames.CanDeleteRoles)]
    public async Task<IActionResult> DeleteConfirmed(string id)
    {
        var role = await roleManager.FindByIdAsync(id);
        if (role == null)
        {
            return NotFound();
        }

        await roleManager.DeleteAsync(role);
        return RedirectToAction(nameof(Index));
    }
}
