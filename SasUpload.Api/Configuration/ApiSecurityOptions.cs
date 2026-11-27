namespace SasUpload.Api.Configuration;

public sealed class ApiSecurityOptions
{
    public const string SectionName = "ApiSecurity";

    public string ApiKey { get; init; } = string.Empty;
}
