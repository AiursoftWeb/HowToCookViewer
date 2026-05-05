using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.HowToCookViewer.Controllers;

/// <summary>
/// This controller is used to change the current culture.
/// </summary>
[LimitPerMin]
public class CultureController : ControllerBase
{
    public IActionResult Set(string culture, string returnUrl)
    {
        if (string.IsNullOrEmpty(culture))
            return BadRequest("Culture cannot be null or empty.");

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
