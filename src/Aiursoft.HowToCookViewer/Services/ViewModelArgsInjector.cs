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
using Aiursoft.UiStack.Views.Shared.Components.SearchForm;
using Aiursoft.UiStack.Views.Shared.Components.SideAdvertisement;
using Aiursoft.UiStack.Views.Shared.Components.Sidebar;
using Aiursoft.UiStack.Views.Shared.Components.SideLogo;
using Microsoft.EntityFrameworkCore;
using Aiursoft.UiStack.Views.Shared.Components.SideMenu;
using Aiursoft.UiStack.Views.Shared.Components.UserDropdown;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Localization;

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
    /// Maps HowToCook repo folder names to (English localizer key, Lucide icon slug).
    /// Display names are looked up via localizer at render time so every language gets the right text.
    /// </summary>
    private static readonly Dictionary<string, (string LocalizerKey, string Icon)> CategoryMeta =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["vegetable_dish"] = ("Vegetable Dishes", "leaf"),
            ["meat_dish"] = ("Meat Dishes", "flame"),
            ["aquatic"] = ("Aquatic", "fish"),
            ["breakfast"] = ("Breakfast", "sunrise"),
            ["staple"] = ("Staple Food", "wheat"),
            ["soup"] = ("Soups", "soup"),
            ["drink"] = ("Drinks", "glass-water"),
            ["dessert"] = ("Desserts", "cake-slice"),
            ["condiment"] = ("Condiments", "droplets"),
            ["semi-finished"] = ("Semi-finished", "package"),
            ["template"] = ("Templates", "file-text"),
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

        // Tip category display names
        _ = localizer["Cooking Tips"];
        _ = localizer["Learn"];
        _ = localizer["Advanced"];

        _ = localizer["My Favorites"];
        _ = localizer["Recipe"];

        // Recipe category display names — translated at nav-build time
        _ = localizer["Vegetable Dishes"];
        _ = localizer["Meat Dishes"];
        _ = localizer["Aquatic"];
        _ = localizer["Breakfast"];
        _ = localizer["Staple Food"];
        _ = localizer["Soups"];
        _ = localizer["Drinks"];
        _ = localizer["Desserts"];
        _ = localizer["Condiments"];
        _ = localizer["Semi-finished"];
        _ = localizer["Templates"];

        // Sort-by nav labels
        _ = localizer["Classification"];
        _ = localizer["By Difficulty"];
        _ = localizer["By Likes"];
        _ = localizer["Most Liked"];
        _ = localizer["Least Liked"];
        _ = localizer["By Comments"];
        _ = localizer["Most Commented"];
        _ = localizer["Least Commented"];
        _ = localizer["By Favorites"];
        _ = localizer["Most Favorited"];
        _ = localizer["Least Favorited"];

        _ = localizer["Recipe Search"];
        _ = localizer["Search recipes (e.g. tomato, egg, tofu…)"];

        _ = localizer["Tip"];

        _ = localizer["Self Host"];

        _ = localizer["Ingredient Reverse Lookup"];
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
                new Link { Text = localizer["Aiursoft"], Href = "https://www.aiursoft.com" },
            ]
        };
        toInject.Navbar = new NavbarViewModel
        {
            ThemeSwitchApiCallEndpoint = "/api/switch-theme",
            SearchForm = new SearchFormViewModel
            {
                SearchUrl = "/Dashboard/Index",
                SearchParam = "q",
                Placeholder = localizer["Search recipes (e.g. tomato, egg, tofu…)"]
            }
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

        // Two separate NavGroups for recipes, mirroring the existing "设置"/"管理" pattern:
        //   NavGroup "All Recipes"  →  CascadedSideBarItem with category sub-links
        //   NavGroup "分类方式"      →  CascadedSideBarItem "By Difficulty" with star sub-links
        var recipeCategories = db.Recipes
            .Select(r => r.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList();

        if (recipeCategories.Count > 0)
        {
            var isOnRecipesController = string.Equals(
                currentViewingController, "Recipes", StringComparison.OrdinalIgnoreCase);
            var currentCategory = context.Request.Query["category"].ToString();
            var currentDifficulty = context.Request.Query["difficulty"].ToString();

            // NavGroup 1: "All Recipes" → flat category links, no collapse needed
            var allRecipesGroup = new NavGroup
            {
                Name = localizer["All Recipes"],
                Items = recipeCategories.Select(cat =>
                {
                    var (localizerKey, icon) = CategoryMeta.TryGetValue(cat, out var meta)
                        ? meta
                        : (cat, "circle-dot");
                    return (SideBarItem)new LinkSideBarItem
                    {
                        LucideIcon = icon,
                        Text = localizer[localizerKey],
                        Href = $"/Recipes/Index?category={Uri.EscapeDataString(cat)}",
                        IsActive = isOnRecipesController &&
                                     string.Equals(currentCategory, cat, StringComparison.OrdinalIgnoreCase)
                    };
                }).ToList()
            };

            // NavGroup 2: "Classification" → By Difficulty, By Likes, By Comments, By Favorites
            var currentSortBy = context.Request.Query["sortBy"].ToString();
            var classificationGroup = new NavGroup
            {
                Name = localizer["Classification"],
                Items =
                [
                    new CascadedSideBarItem
                    {
                        UniqueId   = "recipes-difficulty",
                        LucideIcon = "star",
                        Text       = localizer["By Difficulty"],
                        IsActive   = isOnRecipesController && !string.IsNullOrEmpty(currentDifficulty),
                        Links      = Enumerable.Range(1, 5).Select(stars => new CascadedLink
                        {
                            Text     = new string('★', stars),
                            Href     = $"/Recipes/Index?difficulty={stars}",
                            IsActive = isOnRecipesController && currentDifficulty == stars.ToString()
                        }).ToList()
                    },
                    new CascadedSideBarItem
                    {
                        UniqueId   = "recipes-likes",
                        LucideIcon = "thumbs-up",
                        Text       = localizer["By Likes"],
                        IsActive   = isOnRecipesController && (currentSortBy == "likes_desc" || currentSortBy == "likes_asc"),
                        Links      =
                        [
                            new CascadedLink { Text = localizer["Most Liked"],  Href = "/Recipes/Index?sortBy=likes_desc", IsActive = isOnRecipesController && currentSortBy == "likes_desc" },
                            new CascadedLink { Text = localizer["Least Liked"], Href = "/Recipes/Index?sortBy=likes_asc",  IsActive = isOnRecipesController && currentSortBy == "likes_asc"  }
                        ]
                    },
                    new CascadedSideBarItem
                    {
                        UniqueId   = "recipes-comments",
                        LucideIcon = "message-circle",
                        Text       = localizer["By Comments"],
                        IsActive   = isOnRecipesController && (currentSortBy == "comments_desc" || currentSortBy == "comments_asc"),
                        Links      =
                        [
                            new CascadedLink { Text = localizer["Most Commented"],  Href = "/Recipes/Index?sortBy=comments_desc", IsActive = isOnRecipesController && currentSortBy == "comments_desc" },
                            new CascadedLink { Text = localizer["Least Commented"], Href = "/Recipes/Index?sortBy=comments_asc",  IsActive = isOnRecipesController && currentSortBy == "comments_asc"  }
                        ]
                    },
                    new CascadedSideBarItem
                    {
                        UniqueId   = "recipes-favorites",
                        LucideIcon = "heart",
                        Text       = localizer["By Favorites"],
                        IsActive   = isOnRecipesController && (currentSortBy == "favorites_desc" || currentSortBy == "favorites_asc"),
                        Links      =
                        [
                            new CascadedLink { Text = localizer["Most Favorited"],  Href = "/Recipes/Index?sortBy=favorites_desc", IsActive = isOnRecipesController && currentSortBy == "favorites_desc" },
                            new CascadedLink { Text = localizer["Least Favorited"], Href = "/Recipes/Index?sortBy=favorites_asc",  IsActive = isOnRecipesController && currentSortBy == "favorites_asc"  }
                        ]
                    }
                ]
            };

            // Insert both groups after the first nav group (功能).
            var insertAt = navGroupsForView.Count > 0 ? 1 : 0;
            navGroupsForView.Insert(insertAt, classificationGroup);
            navGroupsForView.Insert(insertAt, allRecipesGroup);
        }

        // ── Tips NavGroup ─────────────────────────────────────────────────────
        // Map tip category folder names → (localizer key, lucide icon)
        var tipCategoryMeta = new Dictionary<string, (string Key, string Icon)>(StringComparer.OrdinalIgnoreCase)
        {
            ["learn"] = ("Learn", "book-open"),
            ["advanced"] = ("Advanced", "graduation-cap"),
        };

        var allTips = db.Tips
            .AsNoTracking()
            .Select(t => new { t.Id, t.Title, t.Category })
            .OrderBy(t => t.Title)
            .ToList();

        if (allTips.Count > 0)
        {
            var isOnTipsController = string.Equals(currentViewingController, "Tips", StringComparison.OrdinalIgnoreCase);
            var currentTipId = context.GetRouteValue("id")?.ToString();

            // Load localized tip titles for the current culture.
            var tipCulture = context.Features.Get<IRequestCultureFeature>()?.RequestCulture.Culture.Name ?? string.Empty;
            var tipIds = allTips.Select(t => t.Id).ToList();
            var localizedTipTitles = db.LocalizedTips
                .AsNoTracking()
                .Where(lt => lt.Culture == tipCulture && tipIds.Contains(lt.TipId))
                .Select(lt => new { lt.TipId, lt.LocalizedTitle })
                .ToDictionary(lt => lt.TipId, lt => lt.LocalizedTitle);

            string ResolveTitle(int tipId, string originalTitle) =>
                localizedTipTitles.TryGetValue(tipId, out var locTitle) && !string.IsNullOrWhiteSpace(locTitle)
                    ? locTitle
                    : originalTitle;

            var tipsItems = new List<SideBarItem>();

            // Root tips first (direct links)
            foreach (var tip in allTips.Where(t => t.Category == "root"))
            {
                tipsItems.Add(new LinkSideBarItem
                {
                    LucideIcon = "file-text",
                    Text = ResolveTitle(tip.Id, tip.Title),
                    Href = $"/Tips/Detail/{tip.Id}",
                    IsActive = isOnTipsController && currentTipId == tip.Id.ToString()
                });
            }

            // Category groups (learn, advanced) — each as a collapsible item
            var knownCategories = new[] { "learn", "advanced" };
            foreach (var cat in knownCategories)
            {
                var catTips = allTips.Where(t => t.Category == cat).ToList();
                if (catTips.Count == 0) continue;

                var (catKey, catIcon) = tipCategoryMeta.TryGetValue(cat, out var meta) ? meta : (cat, "folder");
                tipsItems.Add(new CascadedSideBarItem
                {
                    UniqueId = $"tips-{cat}",
                    LucideIcon = catIcon,
                    Text = localizer[catKey],
                    IsActive = isOnTipsController && catTips.Any(t => currentTipId == t.Id.ToString()),
                    Links = catTips.Select(t => new CascadedLink
                    {
                        Text = ResolveTitle(t.Id, t.Title),
                        Href = $"/Tips/Detail/{t.Id}",
                        IsActive = isOnTipsController && currentTipId == t.Id.ToString()
                    }).ToList()
                });
            }

            var tipsGroup = new NavGroup
            {
                Name = localizer["Cooking Tips"],
                Items = tipsItems
            };

            var tipsInsertAt = navGroupsForView.Count > 0 ? 1 : 0;
            navGroupsForView.Insert(tipsInsertAt, tipsGroup);
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
