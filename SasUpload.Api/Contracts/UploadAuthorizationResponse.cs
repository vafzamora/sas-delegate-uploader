namespace SasUpload.Api.Contracts;

/// <summary>Contains a short-lived authorization for uploading exactly one blob.</summary>
public sealed record UploadAuthorizationResponse(
    Uri UploadUrl,
    string BlobName,
    DateTimeOffset ExpiresAt);
