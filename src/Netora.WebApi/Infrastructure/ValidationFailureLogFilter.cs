using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Netora.Application.Common;
using Netora.Application.Logging;

namespace Netora.WebApi.Infrastructure;

/// <summary>
/// Logs validation failures at the API boundary (18.02): every 400 with the <c>validation_failed</c> envelope,
/// from model binding or from the Application validators. Field names only, never the submitted values or messages.
/// </summary>
public sealed class ValidationFailureLogFilter(IActivityLog activity) : IAsyncResultFilter
{
    private const int MaxFields = 20;

    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult { StatusCode: StatusCodes.Status400BadRequest, Value: { } value }
            && ErrorOf(value) is { Code: "validation_failed" } error)
        {
            var fields = error.Details is null ? "" : string.Join(",", error.Details.Keys.Take(MaxFields));
            var request = context.HttpContext.Request;
            activity.Write(new ActivityEvent(ActivityCategories.Application, "ValidationFailed", ActivityOutcomes.Failure,
                $"{request.Method} {request.Path}", fields.Length == 0 ? null : $"fields={fields}", ActivityLevel.Warning));
        }
        return next();
    }

    private static ApiError? ErrorOf(object value)
    {
        var type = value.GetType();
        if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ApiResponse<>)) return null;
        return type.GetProperty(nameof(ApiResponse<object>.Error))?.GetValue(value) as ApiError;
    }
}
