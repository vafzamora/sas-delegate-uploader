using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SasUpload.Client;

public sealed class UploadCommand
{
    private const string ApiKeyEnvironmentVariable = "SAS_UPLOAD_API_KEY";
    private const string ApiUrlEnvironmentVariable = "SAS_UPLOAD_API_URL";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Usage =>
        "dotnet run --project SasUpload.Client -- --file <path> " +
        "[--api-url <url>]\n\n" +
        $"Set {ApiKeyEnvironmentVariable} with the API key. " +
        $"Optionally set {ApiUrlEnvironmentVariable}; default: http://localhost:5017.";

    private UploadCommand(string filePath, Uri apiUri, string apiKey)
    {
        FilePath = filePath;
        ApiUri = apiUri;
        ApiKey = apiKey;
    }

    private string FilePath { get; }
    private Uri ApiUri { get; }
    private string ApiKey { get; }

    public static UploadCommand Parse(string[] args)
    {
        var filePath = ReadOption(args, "--file")
            ?? throw new ArgumentException("The --file option is required.");
        var apiUrl = ReadOption(args, "--api-url")
            ?? Environment.GetEnvironmentVariable(ApiUrlEnvironmentVariable)
            ?? "http://localhost:5017";
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);

        if (!File.Exists(filePath))
        {
            throw new ArgumentException($"The file '{filePath}' does not exist.");
        }

        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out var apiUri))
        {
            throw new ArgumentException($"'{apiUrl}' is not a valid absolute API URL.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException(
                $"Set the {ApiKeyEnvironmentVariable} environment variable before running the client.");
        }

        return new UploadCommand(filePath, apiUri, apiKey);
    }

    public async Task RunAsync()
    {
        var file = new FileInfo(FilePath);
        var contentType = GetContentType(file.Extension);
        var authorization = await RequestAuthorizationAsync(file, contentType);

        if (authorization.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new HttpRequestException("The upload authorization is already expired.");
        }

        await using var stream = file.OpenRead();
        var blobClient = new BlobClient(authorization.UploadUrl);
        var uploadOptions = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
            Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
            TransferOptions = new StorageTransferOptions
            {
                InitialTransferSize = 8 * 1024 * 1024,
                MaximumConcurrency = 4
            }
        };

        await blobClient.UploadAsync(stream, uploadOptions);

        Console.WriteLine($"Upload completed: {authorization.BlobName}");
        Console.WriteLine($"SAS expiration: {authorization.ExpiresAt:O}");
    }

    private async Task<UploadAuthorizationResponse> RequestAuthorizationAsync(
        FileInfo file,
        string contentType)
    {
        using var httpClient = new HttpClient { BaseAddress = ApiUri };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/uploads/authorize");
        request.Headers.Add("X-Api-Key", ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = JsonContent.Create(
            new CreateUploadAuthorizationRequest(
                file.Name,
                contentType,
                file.Length),
            options: JsonOptions);

        using var response = await httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"The authorization API returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
        }

        return JsonSerializer.Deserialize<UploadAuthorizationResponse>(body, JsonOptions)
            ?? throw new HttpRequestException("The authorization API returned an empty response.");
    }

    private static string GetContentType(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".json" => "application/json",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };

    private static string? ReadOption(string[] args, string name)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--"))
            {
                throw new ArgumentException($"The {name} option requires a value.");
            }

            return args[index + 1];
        }

        return null;
    }
}

public sealed record CreateUploadAuthorizationRequest(
    string FileName,
    string ContentType,
    long FileSizeBytes);

public sealed record UploadAuthorizationResponse(
    Uri UploadUrl,
    string BlobName,
    DateTimeOffset ExpiresAt);
