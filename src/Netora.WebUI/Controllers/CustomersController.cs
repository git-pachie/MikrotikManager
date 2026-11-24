using Microsoft.AspNetCore.Mvc;

namespace Netora.WebUI.Controllers;

public class CustomersController : Controller
{
    [HttpGet("/customers")]
    public IActionResult Index() => View();
}
