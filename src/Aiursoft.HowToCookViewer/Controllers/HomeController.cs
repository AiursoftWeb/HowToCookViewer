using Aiursoft.HowToCookViewer.Models.HomeViewModels;
using Aiursoft.HowToCookViewer.Services;
using Aiursoft.WebTools.Attributes;
using Microsoft.AspNetCore.Mvc;

namespace Aiursoft.HowToCookViewer.Controllers;

[LimitPerMin]
public class HomeController : Controller
{
    public IActionResult Index()
    {
        return this.SimpleView(new IndexViewModel());
    }
}
