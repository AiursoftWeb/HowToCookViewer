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
                options.User.RequireUniqueEmail = true;
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
        var accountSynchronizer = context.HttpContext.RequestServices.GetRequiredService<OidcAccountSynchronizer>();
        var appSettings = context.HttpContext.RequestServices.GetRequiredService<IOptions<AppSettings>>().Value;
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Startup>>();
        var principal = context.Principal!;

        var username = principal.FindFirst(appSettings.OIDC.UsernamePropertyName)?.Value;
        var displayName = principal.FindFirst(appSettings.OIDC.UserDisplayNamePropertyName)?.Value;
        var email = principal.FindFirst(appSettings.OIDC.EmailPropertyName)?.Value;
        var providerKey = principal.FindFirst(appSettings.OIDC.UserIdentityPropertyName)?.Value;
        logger.LogInformation(
            "User '{Username}' from OIDC with email '{Email}' is trying to log in. Provider key: '{ProviderKey}'",
            username, email, providerKey);

        if (
            string.IsNullOrEmpty(username) ||
            string.IsNullOrEmpty(displayName) ||
            string.IsNullOrEmpty(email) ||
            string.IsNullOrEmpty(providerKey))
        {
            context.Fail("Could not find the required username, displayName, email, or sub claim in the OIDC token.");
            return;
        }

        var oidcRoles = principal.FindAll(appSettings.OIDC.RolePropertyName).Select(c => c.Value).ToHashSet();
        if (!string.IsNullOrWhiteSpace(appSettings.DefaultRole))
        {
            logger.LogInformation("Add the default role '{Role}' to the user.", appSettings.DefaultRole);
            oidcRoles.Add(appSettings.DefaultRole);
        }

        var syncResult = await accountSynchronizer.SynchronizeAsync(new OidcUserProfile(
            LoginProvider: context.Scheme.Name,
            ProviderKey: providerKey,
            UserName: username,
            DisplayName: displayName,
            Email: email,
            Roles: oidcRoles));
        if (!syncResult.Succeeded)
        {
            var errors = string.Join(", ", syncResult.Errors.Select(error => error.Description));
            context.Fail($"Failed to synchronize the OIDC account: {errors}");
        }
    }
}
