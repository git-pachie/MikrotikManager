using Microsoft.AspNetCore.Mvc;

namespace Netora.WebUI.Controllers;

public class HomeController : Controller
{
    [HttpGet("/")]
    public IActionResult Index() => Redirect("/customers");

    [HttpGet("/error")]
    public IActionResult Error() => View(new Models.ErrorViewModel
    {
        RequestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier
    });
}
