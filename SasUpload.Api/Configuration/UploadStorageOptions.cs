namespace SasUpload.Api.Configuration;

public sealed class UploadStorageOptions
{
    public const string SectionName = "UploadStorage";

    public string AccountEndpoint { get; init; } = string.Empty;
    public string ContainerName { get; init; } = string.Empty;
    public int SasLifetimeMinutes { get; init; } = 5;
    public long MaxUploadSizeBytes { get; init; } = 500 * 1024 * 1024;
    public string[] AllowedExtensions { get; init; } =
        [".txt", ".pdf", ".png", ".jpg", ".jpeg", ".json", ".zip"];
    public string[] AllowedContentTypes { get; init; } =
        ["text/plain", "application/pdf", "image/png", "image/jpeg", "application/json", "application/zip"];
}
