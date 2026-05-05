using Aiursoft.Scanner.Abstractions;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Controllers;
using Aiursoft.HowToCookViewer.Entities;
using Aiursoft.HowToCookViewer.Services.Authentication;
using Aiursoft.HowToCookViewer.Services.FileStorage;
using Aiursoft.UiStack.Layout;
using Aiursoft.UiStack.Navigation;
using Aiursoft.UiStack.Views.Shared.Components.FooterMenu;
using Aiursoft.UiStack.Views.Shared.Components.LanguagesDropdown;
using Aiursoft.UiStack.Views.Shared.Components.MegaMenu;
using Aiursoft.UiStack.Views.Shared.Components.Navbar;
using Aiursoft.UiStack.Views.Shared.Components.SideAdvertisement;
using Aiursoft.UiStack.Views.Shared.Components.Sidebar;
using Aiursoft.UiStack.Views.Shared.Components.SideLogo;
using Aiursoft.UiStack.Views.Shared.Components.SideMenu;
using Aiursoft.UiStack.Views.Shared.Components.UserDropdown;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;

namespace Aiursoft.HowToCookViewer.Services;

public class ViewModelArgsInjector(
    IStringLocalizer<ViewModelArgsInjector> localizer,
    StorageService storageService,
    NavigationState<Startup> navigationState,
    IAuthorizationService authorizationService,
    IOptions<AppSettings> appSettings,
    GlobalSettingsService globalSettingsService,
    TemplateDbContext db,
    SignInManager<User> signInManager) : IScopedDependency
{
    /// <summary>
    /// Maps HowToCook repo folder names to (Chinese display name, Lucide icon slug).
    /// The folder structure of the repo is stable, so this dictionary is
    /// maintained once and covers all known categories.
    /// </summary>
    private static readonly Dictionary<string, (string DisplayName, string Icon)> CategoryMeta =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["vegetable_dish"] = ("素菜",   "leaf"),
            ["meat_dish"]      = ("荤菜",   "flame"),
            ["aquatic"]        = ("水产",   "fish"),
            ["breakfast"]      = ("早餐",   "sunrise"),
            ["staple"]         = ("主食",   "wheat"),
            ["soup"]           = ("汤品",   "soup"),
            ["drink"]          = ("饮料",   "glass-water"),
            ["dessert"]        = ("甜品",   "cake-slice"),
            ["condiment"]      = ("调料",   "droplets"),
            ["semi-finished"]  = ("半成品", "package"),
            ["template"]       = ("模板",   "file-text"),
        };

    [ExcludeFromCodeCoverage]
    // ReSharper disable once UnusedMember.Local
    private void _useless_for_localizer()
    {
        // Titles, navbar strings.
        _ = localizer["Features"];
        _ = localizer["Index"];
        _ = localizer["Directory"];
        _ = localizer["Users"];
        _ = localizer["Roles"];
        _ = localizer["Administration"];
        _ = localizer["System"];
        _ = localizer["Info"];
        _ = localizer["Manage"];
        _ = localizer["Login"];
        _ = localizer["System Info"];
        _ = localizer["Create User"];
        _ = localizer["User Details"];
        _ = localizer["Edit User"];
        _ = localizer["Delete User"];
        _ = localizer["Create Role"];
        _ = localizer["Role Details"];
        _ = localizer["Edit Role"];
        _ = localizer["Delete Role"];
        _ = localizer["Change Profile"];
        _ = localizer["Change Avatar"];
        _ = localizer["Change Password"];
        _ = localizer["Home"];
        _ = localizer["Settings"];
        _ = localizer["Profile Settings"];
        _ = localizer["Personal"];
        _ = localizer["Unauthorized"];
        _ = localizer["Error"];
        _ = localizer["Permissions"];
        _ = localizer["Background Jobs"];
        _ = localizer["Global Settings"];

        _ = localizer["Access Denied"];
        _ = localizer["Bad Request"];
        _ = localizer["Dashboard"];
        _ = localizer["Internal Server Error"];
        _ = localizer["Lockout"];
        _ = localizer["Not Found"];
        _ = localizer["Permission Details"];
        _ = localizer["Register"];

        _ = localizer["Recipes"];
        _ = localizer["All Recipes"];
    }

    public void InjectSimple(
        HttpContext context,
        UiStackLayoutViewModel toInject)
    {
        toInject.PageTitle = localizer[toInject.PageTitle ?? "View"];
        toInject.AppName = globalSettingsService.GetSettingValueAsync(SettingsMap.ProjectName).GetAwaiter().GetResult();
        toInject.Theme = UiTheme.Light;
        toInject.SidebarTheme = UiSidebarTheme.Default;
        toInject.Layout = UiLayout.Fluid;
        toInject.ContentNoPadding = true;
    }

    public void Inject(
        HttpContext context,
        UiStackLayoutViewModel toInject)
    {
        var preferDarkTheme = context.Request.Cookies[ThemeController.ThemeCookieKey] == true.ToString();
        var projectName = globalSettingsService.GetSettingValueAsync(SettingsMap.ProjectName).GetAwaiter().GetResult();
        var brandName = globalSettingsService.GetSettingValueAsync(SettingsMap.BrandName).GetAwaiter().GetResult();
        var brandHomeUrl = globalSettingsService.GetSettingValueAsync(SettingsMap.BrandHomeUrl).GetAwaiter().GetResult();
        toInject.PageTitle = localizer[toInject.PageTitle ?? "View"];
        toInject.AppName = projectName;
        toInject.Theme = preferDarkTheme ? UiTheme.Dark : UiTheme.Light;
        toInject.SidebarTheme = preferDarkTheme ? UiSidebarTheme.Dark : UiSidebarTheme.Default;
        toInject.Layout = UiLayout.Fluid;
        toInject.FooterMenu = new FooterMenuViewModel
        {
            AppBrand = new Link { Text = brandName, Href = brandHomeUrl },
            Links =
            [
                new Link { Text = localizer["Home"], Href = "/" },
                new Link { Text = "Aiursoft", Href = "https://www.aiursoft.com" },
            ]
        };
        toInject.Navbar = new NavbarViewModel
        {
            ThemeSwitchApiCallEndpoint = "/api/switch-theme"
        };

        var currentViewingController = context.GetRouteValue("controller")?.ToString();
        var navGroupsForView = new List<NavGroup>();

        foreach (var groupDef in navigationState.NavMap)
        {
            var itemsForView = new List<CascadedSideBarItem>();
            foreach (var itemDef in groupDef.Items)
            {
                var linksForView = new List<CascadedLink>();
                foreach (var linkDef in itemDef.Links)
                {
                    bool isVisible;
                    if (string.IsNullOrEmpty(linkDef.RequiredPolicy))
                    {
                        isVisible = true;
                    }
                    else
                    {
                        var authResult = authorizationService.AuthorizeAsync(context.User, linkDef.RequiredPolicy).GetAwaiter().GetResult();
                        isVisible = authResult.Succeeded;
                    }

                    if (isVisible)
                    {
                        linksForView.Add(new CascadedLink
                        {
                            Href = linkDef.Href,
                            Text = localizer[linkDef.Text]
                        });
                    }
                }

                if (linksForView.Any())
                {
                    itemsForView.Add(new CascadedSideBarItem
                    {
                        UniqueId = itemDef.UniqueId,
                        Text = localizer[itemDef.Text],
                        LucideIcon = itemDef.Icon,
                        IsActive = linksForView.Any(l =>
                        {
                            // Extract controller name from href (e.g., "/Manage/Index" -> "Manage")
                            var hrefController = l.Href.TrimStart('/').Split('/').FirstOrDefault();
                            // Exact match to avoid false positives like "Manage" matching "ManagePayroll"
                            return string.Equals(hrefController, currentViewingController, StringComparison.OrdinalIgnoreCase);
                        }),
                        Links = linksForView
                    });
                }
            }

            if (itemsForView.Any())
            {
                navGroupsForView.Add(new NavGroup
                {
                    Name = localizer[groupDef.Name],
                    Items = itemsForView.Select(t => (SideBarItem)t).ToList()
                });
            }
        }

        // Dynamic recipes group: query distinct categories from DB,
        // each becomes its own LinkSideBarItem (no collapse nesting).
        // Inserted after the first group ("功能") so it appears second in the sidebar.
        var recipeCategories = db.Recipes
            .Select(r => r.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        if (recipeCategories.Count > 0)
        {
            var isOnRecipesController = string.Equals(
                currentViewingController, "Recipes", StringComparison.OrdinalIgnoreCase);
            var currentCategory = context.Request.Query["category"].ToString(); // "" when absent

            var categoryItems = new List<SideBarItem>
            {
                new LinkSideBarItem
                {
                    LucideIcon = "utensils",
                    Text = localizer["All Recipes"],
                    Href = "/Recipes/Index",
                    IsActive = isOnRecipesController && string.IsNullOrEmpty(currentCategory)
                }
            };

            categoryItems.AddRange(recipeCategories.Select(cat =>
            {
                var (displayName, icon) = CategoryMeta.TryGetValue(cat, out var meta)
                    ? meta
                    : (cat, "circle-dot");
                return (SideBarItem)new LinkSideBarItem
                {
                    LucideIcon = icon,
                    Text = displayName,
                    Href = $"/Recipes/Index?category={Uri.EscapeDataString(cat)}",
                    IsActive = isOnRecipesController &&
                               string.Equals(currentCategory, cat, StringComparison.OrdinalIgnoreCase)
                };
            }));

            var recipesGroup = new NavGroup
            {
                Name = localizer["Recipes"],
                Items = categoryItems
            };

            // Insert after the first nav group (功能) if it exists, otherwise prepend.
            if (navGroupsForView.Count > 0)
                navGroupsForView.Insert(1, recipesGroup);
            else
                navGroupsForView.Add(recipesGroup);
        }

        toInject.Sidebar = new SidebarViewModel
        {
            SideLogo = new SideLogoViewModel
            {
                AppName = projectName,
                LogoUrl = GetLogoUrl(context).GetAwaiter().GetResult(),
                Href = "/"
            },
            SideMenu = new SideMenuViewModel
            {
                Groups = navGroupsForView
            }
        };

        var currentCulture = context.Features
            .Get<IRequestCultureFeature>()?
            .RequestCulture.Culture.Name; // zh-CN

        // ReSharper disable once RedundantNameQualifier
        var suppportedCultures = Aiursoft.WebTools.OfficialPlugins.LocalizationPlugin.SupportedCultures
            .Select(c => new LanguageSelection
            {
                Link = $"/Culture/Set?culture={c.Key}&returnUrl={context.Request.Path}",
                Name = c.Value // 中文 - 中国
            })
            .ToArray();

        // ReSharper disable once RedundantNameQualifier
        toInject.Navbar.LanguagesDropdown = new LanguagesDropdownViewModel
        {
            Languages = suppportedCultures,
            SelectedLanguage = new LanguageSelection
            {
                Name = Aiursoft.WebTools.OfficialPlugins.LocalizationPlugin.SupportedCultures[currentCulture ?? "en-US"],
                Link = "#",
            }
        };

        if (signInManager.IsSignedIn(context.User))
        {
            var avatarPath = context.User.Claims.First(c => c.Type == UserClaimsPrincipalFactory.AvatarClaimType)
                .Value;
            toInject.Navbar.UserDropdown = new UserDropdownViewModel
            {
                UserName = context.User.Claims.First(c => c.Type == UserClaimsPrincipalFactory.DisplayNameClaimType).Value,
                UserAvatarUrl = $"{storageService.RelativePathToInternetUrl(avatarPath)}?w=100&square=true",
                IconLinkGroups =
                [
                    new IconLinkGroup
                    {
                        Links =
                        [
                            new IconLink { Icon = "user", Text = localizer["Profile"], Href = "/Manage" },
                        ]
                    },
                    new IconLinkGroup
                    {
                        Links =
                        [
                            new IconLink { Icon = "log-out", Text = localizer["Sign out"], Href = "/Account/Logoff" }
                        ]
                    }
                ]
            };
        }
        else
        {
            toInject.Sidebar.SideAdvertisement = new SideAdvertisementViewModel
            {
                Title = localizer["Login"],
                Description = localizer["Login to get access to all features."],
                Href = "/Account/Login",
                ButtonText = localizer["Login"]
            };

            var allowRegister = appSettings.Value.Local.AllowRegister;
            var links = new List<IconLink>
            {
                new()
                {
                    Text = localizer["Login"],
                    Href = "/Account/Login",
                    Icon = "user"
                }
            };
            if (allowRegister && appSettings.Value.LocalEnabled)
            {
                links.Add(new IconLink
                {
                    Text = localizer["Register"],
                    Href = "/Account/Register",
                    Icon = "user-plus"
                });
            }
            toInject.Navbar.UserDropdown = new UserDropdownViewModel
            {
                UserName = localizer["Click to login"],
                UserAvatarUrl = string.Empty,
                IconLinkGroups =
                [
                    new IconLinkGroup
                    {
                        Links = links.ToArray()
                    }
                ]
            };
        }
    }


    private async Task<string> GetLogoUrl(HttpContext context)
    {
        var logoPath = await globalSettingsService.GetSettingValueAsync(SettingsMap.ProjectLogo);
        if (string.IsNullOrWhiteSpace(logoPath))
        {
            return "/logo.svg";
        }
        return storageService.RelativePathToInternetUrl(logoPath, context);
    }
}
