using Microsoft.AspNetCore.Mvc;
using Netora.Application.Common;

namespace Netora.WebApi.Infrastructure;

public static class ApiBehaviorSetup
{
    /// <summary>Returns model-validation failures as a 400 using the standard envelope.</summary>
    public static IMvcBuilder UseApiResponseEnvelopeForValidation(this IMvcBuilder builder)
        => builder.ConfigureApiBehaviorOptions(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var details = context.ModelState
                    .Where(e => e.Value is { Errors.Count: > 0 })
                    .ToDictionary(
                        e => e.Key,
                        e => e.Value!.Errors.Select(x => string.IsNullOrEmpty(x.ErrorMessage) ? "Invalid value." : x.ErrorMessage).ToArray());

                return new BadRequestObjectResult(ApiResponse<object>.Fail(
                    "validation_failed", "One or more validation errors occurred.",
                    context.HttpContext.TraceIdentifier, details));
            };
        });
}
