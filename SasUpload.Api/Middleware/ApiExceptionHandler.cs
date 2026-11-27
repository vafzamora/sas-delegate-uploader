using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace SasUpload.Api.Middleware;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ArgumentException)
        {
            return false;
        }

        logger.LogWarning(exception, "Rejected invalid upload authorization request.");

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid upload request",
                Detail = "The upload metadata is not allowed by this service.",
                Instance = httpContext.Request.Path
            },
            cancellationToken);

        return true;
    }
}
