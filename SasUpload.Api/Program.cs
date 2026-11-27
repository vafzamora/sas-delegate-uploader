using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authentication;
using SasUpload.Api.Configuration;
using SasUpload.Api.Endpoints;
using SasUpload.Api.Middleware;
using SasUpload.Api.Security;
using SasUpload.Api.Services;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddValidation();
builder.Services.AddAuthorization();
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName,
        _ => { });

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
    options.SerializerOptions.PropertyNameCaseInsensitive = false;
    options.SerializerOptions.AllowDuplicateProperties = false;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services
    .AddOptions<ApiSecurityOptions>()
    .BindConfiguration(ApiSecurityOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), "ApiSecurity:ApiKey is required.")
    .ValidateOnStart();

builder.Services
    .AddOptions<UploadStorageOptions>()
    .BindConfiguration(UploadStorageOptions.SectionName)
    .Validate(options => Uri.TryCreate(options.AccountEndpoint, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp),
        "UploadStorage:AccountEndpoint must be an absolute HTTP or HTTPS URI.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.ContainerName),
        "UploadStorage:ContainerName is required.")
    .Validate(options => options.SasLifetimeMinutes is >= 1 and <= 60,
        "UploadStorage:SasLifetimeMinutes must be between 1 and 60.")
    .Validate(options => options.MaxUploadSizeBytes is > 0 and <= 5L * 1024 * 1024 * 1024,
        "UploadStorage:MaxUploadSizeBytes must be between 1 and 5 GiB.")
    .ValidateOnStart();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<BlobServiceClient>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<Microsoft.Extensions.Options.IOptions<UploadStorageOptions>>()
        .Value;
    var credential = new DefaultAzureCredential(options: new DefaultAzureCredentialOptions()
    {
        ExcludeVisualStudioCredential = true,
    });

    var token = credential.GetToken(
        new TokenRequestContext(
            ["https://storage.azure.com/.default"]));

    return new BlobServiceClient(
        new Uri(options.AccountEndpoint),
        credential);
});
builder.Services.AddScoped<IUploadSasService, UploadSasService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapUploadEndpoints();

await app.RunAsync();
