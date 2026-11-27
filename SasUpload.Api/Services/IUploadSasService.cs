using SasUpload.Api.Contracts;

namespace SasUpload.Api.Services;

public interface IUploadSasService
{
    Task<UploadAuthorizationResponse> AuthorizeAsync(
        CreateUploadAuthorizationRequest request,
        string clientId,
        CancellationToken cancellationToken);
}
