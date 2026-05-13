using System.Diagnostics.CodeAnalysis;
using Aiursoft.HowToCookViewer.Authorization;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Aiursoft.HowToCookViewer.Services.Authentication;

[ExcludeFromCodeCoverage]
public static class AuthenticationExtensions
{
    public static IServiceCollection AddTemplateAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var appSettings = configuration.GetSection("AppSettings").Get<AppSettings>()!;
        services.AddIdentity<User, IdentityRole>(options =>
            {
                if (appSettings.LocalEnabled && appSettings.Local.AllowWeakPassword)
                {
                    options.Password.RequireNonAlphanumeric = false;
                    options.Password.RequireDigit = false;
                    options.Password.RequiredLength = 6;
                    options.Password.RequiredUniqueChars = 0;
                    options.Password.RequireLowercase = false;
                    options.Password.RequireUppercase = false;
                }
                else
                {
                    options.Password.RequireNonAlphanumeric = true;
                    options.Password.RequireDigit = true;
                    options.Password.RequiredLength = 8;
                    options.Password.RequiredUniqueChars = 1;
                    options.Password.RequireLowercase = true;
                    options.Password.RequireUppercase = true;
                }
            })
            .AddEntityFrameworkStores<TemplateDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IUserClaimsPrincipalFactory<User>, UserClaimsPrincipalFactory>();

        var authBuilder = services.AddAuthentication(options =>
        {
            options.DefaultScheme = IdentityConstants.ApplicationScheme;
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        });

        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logoff";
            options.AccessDeniedPath = "/Error/Code403";
        });

        if (appSettings.OIDCEnabled)
        {
            authBuilder.AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                options.Authority = appSettings.OIDC.Authority;
                options.ClientId = appSettings.OIDC.ClientId;
                options.ClientSecret = appSettings.OIDC.ClientSecret;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.SignInScheme = IdentityConstants.ExternalScheme;

                options.Scope.Clear();
                options.Scope.Add("openid");
                options.Scope.Add("profile");
                options.Scope.Add("email");

                options.SaveTokens = true;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    NameClaimType = appSettings.OIDC.UsernamePropertyName,
                    RoleClaimType = appSettings.OIDC.RolePropertyName
                };

                options.Events = new OpenIdConnectEvents
                {
                    OnTokenValidated = SyncOidcContext,
                    OnRemoteFailure = HandleRemoteFailure
                };
            });
        }

        services.AddAuthorization(options =>
        {
            foreach (var permission in AppPermissions.GetAllPermissions())
            {
                options.AddPolicy(
                    name: permission.Key,
                    policy => policy.RequireClaim(AppPermissions.Type, permission.Key));
            }
        });
        return services;
    }

    /// <summary>
    /// Handle OIDC remote failures (fallback logic for correlation failures or invalid grants)
    /// </summary>
    private static async Task HandleRemoteFailure(RemoteFailureContext context)
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Startup>>();

        // Check if the exception is an OIDC protocol exception
        if (context.Failure is OpenIdConnectProtocolException)
        {
            logger.LogWarning(context.Failure, "OIDC protocol error detected (likely invalid_grant or dirty cookie). Initiating cleanup and redirect.");

            // 1. Mark the exception as handled to prevent the yellow screen of death
            context.HandleResponse();

            // 2. Force cleanup of local cookies
            // Clear External Scheme (temporary cookie used by OIDC middleware)
            await context.HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            // Clear Application Scheme (to prevent stale login state)
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

            // 3. Redirect back to login page to let user retry with a fresh request
            context.Response.Redirect("/Account/Login");
        }
        else
        {
            // If it's a different type of error, log it and allow default behavior
            logger.LogError(context.Failure, "An unexpected error occurred during OIDC remote authentication.");
        }
    }

    private static async Task SyncOidcContext(TokenValidatedContext context)
    {
        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<User>>();
        var roleManager = context.HttpContext.RequestServices.GetRequiredService<RoleManager<IdentityRole>>();
        var appSettings = context.HttpContext.RequestServices.GetRequiredService<IOptions<AppSettings>>().Value;
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Startup>>();
        var principal = context.Principal!;

        // 1. Get the user's information from the OIDC token
        var username = principal.FindFirst(appSettings.OIDC.UsernamePropertyName)?.Value;
        var displayName = principal.FindFirst(appSettings.OIDC.UserDisplayNamePropertyName)?.Value;
        var email = principal.FindFirst(appSettings.OIDC.EmailPropertyName)?.Value;
        var providerKey = principal.FindFirst(appSettings.OIDC.UserIdentityPropertyName)?.Value;
        logger.LogInformation(
            "User '{Username}' from OIDC with email '{Email}' is trying to log in. Provider key: '{ProviderKey}'",
            username, email, providerKey);

        // 2. Ensure the user's information is valid
        if (
            string.IsNullOrEmpty(username) ||
            string.IsNullOrEmpty(displayName) ||
            string.IsNullOrEmpty(email) ||
            string.IsNullOrEmpty(providerKey))
        {
            context.Fail("Could not find the required username, displayName, email, or sub claim in the OIDC token.");
            return;
        }

        // 3. Try to find the user in the local database
        var loginInfo = new UserLoginInfo(context.Scheme.Name, providerKey, context.Scheme.Name);
        logger.LogInformation(
            "Try to find the user in the local database. With username: '{Username}', email: '{Email}', provider key: '{ProviderKey}'",
            username, email, providerKey);
        var localUser = await userManager.FindByLoginAsync(loginInfo.LoginProvider, loginInfo.ProviderKey) ??
                        await userManager.FindByNameAsync(username) ??
                        await userManager.FindByEmailAsync(email);

        // 4. If the user doesn't exist, create a new one
        if (localUser is null)
        {
            localUser = new User
            {
                UserName = username,
                DisplayName = displayName,
                Email = email,
            };
            logger.LogInformation(
                "The user with name '{Username}' and email '{Email}' doesn't exist in the local database. Create a new one.",
                username, email);
            var createUserResult = await userManager.CreateAsync(localUser);
            if (!createUserResult.Succeeded)
            {
                var errors = string.Join(", ", createUserResult.Errors.Select(e => e.Description));
                context.Fail($"Failed to create a local user: {errors}");
                return;
            }
        }

        // 5. Patch the user's information if needed
        if (!string.Equals(localUser.UserName, username, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Setting the user's username to '{Username}' from OIDC.", username);
            await userManager.SetUserNameAsync(localUser, username);
        }

        if (!string.Equals(localUser.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Setting the user's display name to '{DisplayName}' from OIDC.", displayName);
            localUser.DisplayName = displayName;
            await userManager.UpdateAsync(localUser);
        }

        if (!string.Equals(localUser.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Setting the user's email to '{Email}' from OIDC.", email);
            await userManager.SetEmailAsync(localUser, email);
            localUser.EmailConfirmed = true;
            await userManager.UpdateAsync(localUser);
        }

        // 6. Add the user's login information if needed
        var userLogins = await userManager.GetLoginsAsync(localUser);
        if (!userLogins.Any(l => l.LoginProvider == loginInfo.LoginProvider && l.ProviderKey == loginInfo.ProviderKey))
        {
            logger.LogInformation(
                "Adding the user's login information with provider '{Provider}' and key '{Key}' from OIDC.",
                loginInfo.LoginProvider, loginInfo.ProviderKey);
            await userManager.AddLoginAsync(localUser, loginInfo);
        }

        // 7. Add the default role based on settings
        var oidcRoles = principal.FindAll(appSettings.OIDC.RolePropertyName).Select(c => c.Value).ToHashSet();
        if (!string.IsNullOrWhiteSpace(appSettings.DefaultRole))
        {
            logger.LogInformation("Add the default role '{Role}' to the user.", appSettings.DefaultRole);
            oidcRoles.Add(appSettings.DefaultRole);
        }

        // 8. Add or remove roles based on the user's roles in OIDC and local database.'
        var localRoles = (await userManager.GetRolesAsync(localUser)).ToHashSet();
        var rolesToAdd = oidcRoles.Except(localRoles);
        foreach (var roleName in rolesToAdd)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                logger.LogInformation("The role '{Role}' doesn't exist. Create a new one.", roleName);
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }

            logger.LogInformation("Add the role '{Role}' to the user.", roleName);
            await userManager.AddToRoleAsync(localUser, roleName);
        }

        var rolesToRemove = localRoles.Except(oidcRoles).ToArray();
        if (rolesToRemove.Any())
        {
            logger.LogInformation("Remove the roles '{Roles}' from the user.", string.Join(", ", rolesToRemove));
            await userManager.RemoveFromRolesAsync(localUser, rolesToRemove);
        }
    }
}
