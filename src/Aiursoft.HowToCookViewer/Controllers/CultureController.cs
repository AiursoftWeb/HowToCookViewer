using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace Aiursoft.HowToCookViewer.Controllers;

/// <summary>
/// This controller is used to change the current culture.
/// </summary>
[LimitPerMin]
public class CultureController(IStringLocalizer<CultureController> localizer) : ControllerBase
{
    public IActionResult Set(string culture, string returnUrl)
    {
        if (string.IsNullOrEmpty(culture))
            return BadRequest(localizer["Culture cannot be null or empty."]);

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions { Expires = DateTimeOffset.UtcNow.AddYears(1) }
        );

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction("Index", "Home");
    }
}
