using System.Security.Cryptography;
using System.Text;
using Aiursoft.Scanner.Abstractions;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Aiursoft.HowToCookViewer.Services.Authentication;

public sealed record OidcUserProfile(
    string LoginProvider,
    string ProviderKey,
    string UserName,
    string DisplayName,
    string Email,
    IReadOnlySet<string> Roles);

public class OidcAccountSynchronizer(
    UserManager<User> userManager,
    RoleManager<IdentityRole> roleManager,
    TemplateDbContext dbContext,
    ILogger<OidcAccountSynchronizer> logger) : IScopedDependency
{
    public async Task<IdentityResult> SynchronizeAsync(
        OidcUserProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (profile.Roles.Any(role => string.IsNullOrWhiteSpace(role) || role.Length > 256))
        {
            return Failed("InvalidOidcRole", "OIDC role names must contain between 1 and 256 characters.");
        }

        IDbContextTransaction? transaction = null;
        if (dbContext.Database.IsRelational())
        {
            transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        }

        await using (transaction)
        {
            try
            {
                var result = await SynchronizeCoreAsync(profile);
                if (!result.Succeeded)
                {
                    if (transaction is not null)
                    {
                        await transaction.RollbackAsync(cancellationToken);
                    }

                    return result;
                }

                if (transaction is not null)
                {
                    await transaction.CommitAsync(cancellationToken);
                }

                return IdentityResult.Success;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "OIDC account synchronization failed for provider {Provider}.", profile.LoginProvider);
                if (transaction is not null)
                {
                    await transaction.RollbackAsync(cancellationToken);
                }

                return Failed("OidcSynchronizationFailed", "OIDC account synchronization failed.");
            }
        }
    }

    private async Task<IdentityResult> SynchronizeCoreAsync(OidcUserProfile profile)
    {
        var localUser = await userManager.FindByLoginAsync(profile.LoginProvider, profile.ProviderKey);
        var needsLoginBinding = localUser is null;

        if (localUser is null)
        {
            localUser = await userManager.FindByEmailAsync(profile.Email);
        }

        if (localUser is null)
        {
            localUser = Activator.CreateInstance<User>();
            localUser.UserName = await GetAvailableUserNameAsync(profile.UserName, profile.ProviderKey);
            localUser.DisplayName = profile.DisplayName;
            localUser.Email = profile.Email;
            localUser.EmailConfirmed = true;

            var createResult = await userManager.CreateAsync(localUser);
            if (!createResult.Succeeded)
            {
                return createResult;
            }
        }
        else
        {
            localUser.DisplayName = profile.DisplayName;
            localUser.Email = profile.Email;
            localUser.EmailConfirmed = true;
            var updateResult = await userManager.UpdateAsync(localUser);
            if (!updateResult.Succeeded)
            {
                return updateResult;
            }
        }

        if (needsLoginBinding)
        {
            var existingLogins = await userManager.GetLoginsAsync(localUser);
            if (existingLogins.Any(login =>
                    login.LoginProvider == profile.LoginProvider &&
                    login.ProviderKey != profile.ProviderKey))
            {
                return Failed(
                    "OidcEmailAlreadyBound",
                    "The email address is already bound to another subject from this OIDC provider.");
            }

            var addLoginResult = await userManager.AddLoginAsync(
                localUser,
                new UserLoginInfo(profile.LoginProvider, profile.ProviderKey, profile.LoginProvider));
            if (!addLoginResult.Succeeded)
            {
                return addLoginResult;
            }
        }

        foreach (var roleName in profile.Roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var createRoleResult = await roleManager.CreateAsync(new IdentityRole(roleName));
                if (!createRoleResult.Succeeded)
                {
                    return createRoleResult;
                }
            }
        }

        var localRoles = (await userManager.GetRolesAsync(localUser)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var roleName in profile.Roles.Except(localRoles, StringComparer.OrdinalIgnoreCase))
        {
            var addRoleResult = await userManager.AddToRoleAsync(localUser, roleName);
            if (!addRoleResult.Succeeded)
            {
                return addRoleResult;
            }
        }

        var rolesToRemove = localRoles.Except(profile.Roles, StringComparer.OrdinalIgnoreCase).ToArray();
        if (rolesToRemove.Length > 0)
        {
            var removeRolesResult = await userManager.RemoveFromRolesAsync(localUser, rolesToRemove);
            if (!removeRolesResult.Succeeded)
            {
                return removeRolesResult;
            }
        }

        return IdentityResult.Success;
    }

    private async Task<string> GetAvailableUserNameAsync(string requestedUserName, string providerKey)
    {
        if (await userManager.FindByNameAsync(requestedUserName) is null)
        {
            return requestedUserName;
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(providerKey)))[..10].ToLowerInvariant();
        var prefix = requestedUserName.Length > 240 ? requestedUserName[..240] : requestedUserName;
        return $"{prefix}-{hash}";
    }

    private static IdentityResult Failed(string code, string description)
    {
        return IdentityResult.Failed(new IdentityError { Code = code, Description = description });
    }
}
