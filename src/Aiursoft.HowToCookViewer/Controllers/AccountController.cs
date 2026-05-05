using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Models.AccountViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.HowToCookViewer.Controllers;

/// <summary>
/// This controller is used to handle account related actions like login, register, log off.
/// </summary>
[LimitPerMin]
public class AccountController(
    IStringLocalizer<AccountController> localizer,
    IOptions<AppSettings> appSettings,
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    ILogger<AccountController> logger)
    : Controller
{
    private readonly AppSettings _appSettings = appSettings.Value;

    // GET: /Account/Login
    [ExcludeFromCodeCoverage]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (_appSettings.OIDCEnabled)
        {
            var provider = OpenIdConnectDefaults.AuthenticationScheme;
            var redirectUrl = Url.Action(nameof(ExternalLoginCallback), "Account", new { returnUrl });
            var properties = signInManager.ConfigureExternalAuthenticationProperties(provider, redirectUrl);
            return new ChallengeResult(provider, properties);
        }

        ViewData["ReturnUrl"] = returnUrl;
        return this.StackView(new LoginViewModel());
    }

    // POST: /Account/Login
    [ExcludeFromCodeCoverage]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
    {
        if (_appSettings.OIDCEnabled)
        {
            return BadRequest("Local login is disabled when OIDC authentication is enabled.");
        }

        ViewData["ReturnUrl"] = returnUrl;
        if (ModelState.IsValid)
        {
            var possibleUser = await userManager.FindByEmailAsync(model.EmailOrUserName!);
            if (possibleUser == null)
            {
                possibleUser = await userManager.FindByNameAsync(model.EmailOrUserName!);
            }

            if (possibleUser == null)
            {
                logger.LogWarning(0, "Invalid login attempt with username or email: {UsernameOrEmail}", model.EmailOrUserName);
                ModelState.AddModelError(string.Empty, localizer["Invalid login attempt. Please check username and password."]);
                return this.StackView(new LoginViewModel());
            }

            var result =
                await signInManager.PasswordSignInAsync(possibleUser, model.Password!, _appSettings.PersistsSignIn, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                logger.LogInformation(1, "User logged in");
                return RedirectToLocal(returnUrl ?? "/Dashboard/Index");
            }

            if (result.IsLockedOut)
            {
                logger.LogWarning(2, "User account locked out");
                ModelState.AddModelError(string.Empty, localizer["This account has been locked out, please try again later."]);
                return this.StackView(new LockoutViewModel(), "Lockout");
            }

            ModelState.AddModelError(string.Empty, localizer["Invalid login attempt. Please check username and password."]);
        }

        return this.StackView(model);
    }

    // GET: /Account/Register
    [ExcludeFromCodeCoverage]
    [HttpGet]
    public IActionResult Register(string? returnUrl = null)
    {
        // If in OIDC mode or registration is not allowed, return 400.
        if (_appSettings.OIDCEnabled || !_appSettings.Local.AllowRegister)
        {
            return BadRequest("Registration is not allowed in the current configuration.");
        }

        ViewData["ReturnUrl"] = returnUrl;
        return this.StackView(new RegisterViewModel());
    }

    // POST: /Account/Register
    [ExcludeFromCodeCoverage]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model, string? returnUrl = null)
    {
        if (_appSettings.OIDCEnabled || !_appSettings.Local.AllowRegister)
        {
            return BadRequest("Registration is not allowed in the current configuration.");
        }

        ViewData["ReturnUrl"] = returnUrl;
        if (ModelState.IsValid)
        {
            var user = new User
            {
                UserName = model.Email!.Split('@')[0],
                DisplayName = model.Email!.Split('@')[0],
                Email = model.Email,
            };
            var result = await userManager.CreateAsync(user, model.Password!);
            if (result.Succeeded)
            {
                if (!string.IsNullOrWhiteSpace(_appSettings.DefaultRole))
                {
                    var addToRoleResult = await userManager.AddToRoleAsync(user, _appSettings.DefaultRole);
                    if (!addToRoleResult.Succeeded)
                    {
                        AddErrors(addToRoleResult);
                        return this.StackView(model);
                    }
                }

                await signInManager.SignInAsync(user, isPersistent: false);
                logger.LogInformation(3, "User created a new account with password");
                return RedirectToLocal(returnUrl ?? "/Dashboard/Index");
            }

            AddErrors(result);
        }

        return this.StackView(model);
    }

    [ExcludeFromCodeCoverage]
    [Authorize]
    public async Task<IActionResult> LogOff()
    {
        if (_appSettings.OIDCEnabled)
        {
            logger.LogInformation(4, "User logged out with OIDC.");
            var properties = new AuthenticationProperties { RedirectUri = "/" };
            return SignOut(properties, IdentityConstants.ApplicationScheme, OpenIdConnectDefaults.AuthenticationScheme);
        }

        await signInManager.SignOutAsync();
        logger.LogInformation(4, "User logged out locally.");
        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    [ExcludeFromCodeCoverage]
    [HttpGet]
    public async Task<IActionResult> ExternalLoginCallback(string? returnUrl = null, string? remoteError = null)
    {
        if (remoteError != null)
        {
            ModelState.AddModelError(string.Empty, $"Error from external provider: {remoteError}");
            return RedirectToAction(nameof(Login));
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            return RedirectToAction(nameof(Login));
        }

        var result = await signInManager.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey,
            isPersistent: _appSettings.PersistsSignIn, bypassTwoFactor: true);
        if (result.Succeeded)
        {
            logger.LogInformation("User logged in with {Name} provider.", info.LoginProvider);
            return RedirectToLocal(returnUrl ?? "/Dashboard/Index");
        }

        if (result.IsLockedOut)
        {
            return this.StackView(new LockoutViewModel());
        }
        else
        {
            ModelState.AddModelError(string.Empty,
                "Failed to associate external login. The user may not exist locally.");
            return RedirectToAction(nameof(Login));
        }
    }

    #region Helpers

    private void AddErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            if (error.Code == "DuplicateUserName")
            {
                error.Description = localizer["The username already exists. Please try another username."];
            }
            ModelState.AddModelError(string.Empty, error.Description);
        }
    }

    private IActionResult RedirectToLocal(string returnUrl)
    {
        if (Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    #endregion
}
