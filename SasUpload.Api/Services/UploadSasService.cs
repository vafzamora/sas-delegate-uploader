using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using Microsoft.Extensions.Options;
using SasUpload.Api.Configuration;
using SasUpload.Api.Contracts;
using System.IO;

namespace SasUpload.Api.Services;

public sealed class UploadSasService(
    BlobServiceClient blobServiceClient,
    IOptions<UploadStorageOptions> options,
    TimeProvider timeProvider,
    ILogger<UploadSasService> logger)
    : IUploadSasService
{
    private readonly UploadStorageOptions _options = options.Value;

    public async Task<UploadAuthorizationResponse> AuthorizeAsync(
        CreateUploadAuthorizationRequest request,
        string clientId,
        CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.SasLifetimeMinutes);
        var blobName = $"uploads/{SanitizeClientId(clientId)}/{now:yyyy/MM/dd}/{Guid.NewGuid():N}{extension}";
        var containerClient = blobServiceClient.GetBlobContainerClient(_options.ContainerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        var delegationKey = await blobServiceClient.GetUserDelegationKeyAsync(
            now.AddMinutes(-1),
            expiresAt,
            cancellationToken);

        var sasBuilder = new BlobSasBuilder(
            BlobSasPermissions.Create | BlobSasPermissions.Write,
            expiresAt)
        {
            BlobContainerName = _options.ContainerName,
            BlobName = blobName,
            Resource = "b",
            StartsOn = now.AddMinutes(-1),
            Protocol = SasProtocol.Https
        };

        var sasQuery = sasBuilder.ToSasQueryParameters(
            delegationKey.Value,
            blobServiceClient.AccountName);
        var uploadUrl = new UriBuilder(blobClient.Uri) { Query = sasQuery.ToString() }.Uri;

        logger.LogInformation(
            "Issued upload authorization for blob {BlobName}, expiring at {ExpiresAt}.",
            blobName,
            expiresAt);

        return new UploadAuthorizationResponse(uploadUrl, blobName, expiresAt);
    }

    private void ValidateRequest(CreateUploadAuthorizationRequest request)
    {
        if (request.FileSizeBytes > _options.MaxUploadSizeBytes)
        {
            throw new ArgumentException(
                $"The requested file exceeds the configured limit of {_options.MaxUploadSizeBytes} bytes.");
        }

        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
        if (!_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The file extension is not allowed.");
        }

        if (!_options.AllowedContentTypes.Contains(request.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The content type is not allowed.");
        }
    }

    private static string SanitizeClientId(string clientId)
    {
        var sanitized = new string(clientId
            .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_')
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "unknown-client" : sanitized;
    }
}
