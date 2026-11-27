using System.ComponentModel.DataAnnotations;

namespace SasUpload.Api.Contracts;

/// <summary>Describes a file for which an upload authorization is requested.</summary>
public sealed record CreateUploadAuthorizationRequest
{
    /// <summary>The original file name. The service uses only its validated extension.</summary>
    [Required, MaxLength(255)]
    public required string FileName { get; init; }

    /// <summary>The media type that the client intends to store with the blob.</summary>
    [Required, MaxLength(127)]
    public required string ContentType { get; init; }

    /// <summary>The expected file size in bytes.</summary>
    [Range(1, 5L * 1024 * 1024 * 1024)]
    public required long FileSizeBytes { get; init; }
}
