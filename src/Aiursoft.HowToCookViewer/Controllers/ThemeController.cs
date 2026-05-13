using Aiursoft.HowToCookViewer.Models.ManageViewModels;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.HowToCookViewer.Controllers;

/// <summary>
/// This controller is used to handle theme related actions like switch theme.
/// </summary>
[LimitPerMin]
public class ThemeController : ControllerBase
{
    public const string ThemeCookieKey = "prefer-dark";

    [Route("api/switch-theme")]
    [HttpPost]
    public IActionResult SwitchTheme([FromBody] SwitchThemeViewModel model)
    {
        var preferDark = model.Theme == "dark";
        Response.Cookies.Append(
            key: ThemeCookieKey,
            value: preferDark.ToString(),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) }
        );
        return Ok();
    }
}
