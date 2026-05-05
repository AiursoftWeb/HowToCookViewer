using Aiursoft.HowToCookViewer.Authorization;
using Aiursoft.HowToCookViewer.Configuration;
using Aiursoft.HowToCookViewer.Models.GlobalSettingsViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.UiStack.Navigation;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.HowToCookViewer.Controllers;

[Authorize(Policy = AppPermissionNames.CanManageGlobalSettings)]
[LimitPerMin]
public class GlobalSettingsController(GlobalSettingsService settingsService) : Controller
{
    [RenderInNavBar(
        NavGroupName = "Administration",
        NavGroupOrder = 9999,
        CascadedLinksGroupName = "System",
        CascadedLinksIcon = "settings",
        CascadedLinksOrder = 9999,
        LinkText = "Global Settings",
        LinkOrder = 1)]
    public async Task<IActionResult> Index()
    {
        var model = new IndexViewModel();
        foreach (var definition in SettingsMap.Definitions)
        {
            model.Settings.Add(new SettingViewModel
            {
                Key = definition.Key,
                Name = definition.Name,
                Description = definition.Description,
                Type = definition.Type,
                DefaultValue = definition.DefaultValue,
                ChoiceOptions = definition.ChoiceOptions,
                Value = await settingsService.GetSettingValueAsync(definition.Key),
                IsOverriddenByConfig = settingsService.IsOverriddenByConfig(definition.Key),
                // File upload settings
                Subfolder = definition.Subfolder,
                AllowedExtensions = definition.AllowedExtensions,
                MaxSizeInMb = definition.MaxSizeInMb
            });
        }
        return this.StackView(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(EditViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return RedirectToAction(nameof(Index));
        }

        try
        {
            await settingsService.UpdateSettingAsync(model.Key, model.Value ?? string.Empty);
        }
        catch (InvalidOperationException e)
        {
            ModelState.AddModelError(string.Empty, e.Message);
            return RedirectToAction(nameof(Index));
        }

        return RedirectToAction(nameof(Index));
    }
}
