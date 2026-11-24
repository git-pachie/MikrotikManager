using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Netora.WebUI.Services;

namespace Netora.WebUI.Infrastructure;

/// <summary>When the API no longer accepts the user's token, end the browser session and ask for a new sign-in.</summary>
public sealed class ApiUnauthorizedExceptionFilter : IAsyncExceptionFilter
{
    public async Task OnExceptionAsync(ExceptionContext context)
    {
        if (context.Exception is not ApiUnauthorizedException) return;

        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        var request = context.HttpContext.Request;
        var returnUrl = HttpMethods.IsGet(request.Method) ? $"{request.Path}{request.QueryString}" : null;
        context.Result = new RedirectToActionResult("Login", "Account", new { returnUrl, expired = true });
        context.ExceptionHandled = true;
    }
}
