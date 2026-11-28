using Netora.Infrastructure.Router;

namespace Netora.WebApi.Infrastructure;

public sealed class RouterModeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, RouterModeSelection mode)
    {
        if (context.User.Identity?.IsAuthenticated == true && context.GetEndpoint() is not null)
            await mode.LoadAsync(context.RequestAborted);

        await next(context);
    }
}
