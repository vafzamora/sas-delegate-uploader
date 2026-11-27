using Microsoft.AspNetCore.Http.HttpResults;
using SasUpload.Api.Contracts;
using SasUpload.Api.Services;
using System.Security.Claims;

namespace SasUpload.Api.Endpoints;

public static class UploadEndpoints
{
    public static void MapUploadEndpoints(this WebApplication app)
    {
        app.MapPost(
                "/api/uploads/authorize",
                async Task<Ok<UploadAuthorizationResponse>> (
                    CreateUploadAuthorizationRequest request,
                    IUploadSasService service,
                    ClaimsPrincipal user,
                    CancellationToken cancellationToken) =>
                {
                    var clientId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown-client";
                    var response = await service.AuthorizeAsync(request, clientId, cancellationToken);
                    return TypedResults.Ok(response);
                })
            .RequireAuthorization()
            .WithName("AuthorizeBlobUpload")
            .WithSummary("Authorize one blob upload")
            .WithDescription(
                "Returns a short-lived, HTTPS-only SAS restricted to create and write one generated blob name.")
            .Produces<UploadAuthorizationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);
    }
}
