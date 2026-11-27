package com.example.sasupload;

import com.azure.identity.DefaultAzureCredentialBuilder;
import com.azure.storage.blob.BlobServiceClient;
import com.azure.storage.blob.BlobServiceClientBuilder;
import com.azure.storage.blob.BlobContainerClient;
import com.azure.storage.blob.specialized.BlockBlobClient;
import com.azure.storage.blob.sas.BlobSasPermission;
import com.azure.storage.blob.sas.BlobServiceSasSignatureValues;
import com.azure.storage.common.sas.SasProtocol;

import java.net.URI;
import java.time.OffsetDateTime;
import java.time.ZoneOffset;
import java.util.Locale;
import java.util.Set;
import java.util.UUID;

public final class UploadSasService {
    private static final long MAX_CONFIGURED_UPLOAD_SIZE = 5L * 1024 * 1024 * 1024;
    private static final Set<String> ALLOWED_EXTENSIONS =
            Set.of(".txt", ".pdf", ".png", ".jpg", ".jpeg", ".json", ".zip");
    private static final Set<String> ALLOWED_CONTENT_TYPES =
            Set.of("text/plain", "application/pdf", "image/png", "image/jpeg",
                    "application/json", "application/zip");

    private final BlobServiceClient blobServiceClient;
    private final String containerName;
    private final int sasLifetimeMinutes;
    private final long maxUploadSizeBytes;

    public UploadSasService() {
        String accountEndpoint = requiredEnvironment("UploadStorage__AccountEndpoint");
        containerName = environment("UploadStorage__ContainerName", "uploads");
        sasLifetimeMinutes = integerEnvironment("UploadStorage__SasLifetimeMinutes", 5);
        maxUploadSizeBytes = longEnvironment("UploadStorage__MaxUploadSizeBytes", 500L * 1024 * 1024);
        if (sasLifetimeMinutes < 1 || sasLifetimeMinutes > 60) {
            throw new IllegalArgumentException("UploadStorage__SasLifetimeMinutes must be between 1 and 60.");
        }
        if (maxUploadSizeBytes < 1 || maxUploadSizeBytes > MAX_CONFIGURED_UPLOAD_SIZE) {
            throw new IllegalArgumentException("UploadStorage__MaxUploadSizeBytes must be between 1 and 5 GiB.");
        }
        URI endpoint = URI.create(accountEndpoint);
        if (!("http".equalsIgnoreCase(endpoint.getScheme())
                || "https".equalsIgnoreCase(endpoint.getScheme()))) {
            throw new IllegalArgumentException("UploadStorage__AccountEndpoint must be an absolute HTTP or HTTPS URI.");
        }

        blobServiceClient = new BlobServiceClientBuilder()
                .endpoint(accountEndpoint)
                .credential(new DefaultAzureCredentialBuilder().build())
                .buildClient();
    }

    public UploadAuthorization authorize(UploadRequest request) {
        validateRequest(request);

        String extension = extensionOf(request.fileName());
        OffsetDateTime now = OffsetDateTime.now(ZoneOffset.UTC);
        OffsetDateTime expiresAt = now.plusMinutes(sasLifetimeMinutes);
        String blobName = "uploads/poc-console-client/" + now.toLocalDate()
                + "/" + UUID.randomUUID().toString().replace("-", "") + extension;
        BlobContainerClient containerClient = blobServiceClient.getBlobContainerClient(containerName);
        BlockBlobClient blobClient = containerClient.getBlobClient(blobName).getBlockBlobClient();

        var delegationKey = blobServiceClient.getUserDelegationKey(now.minusMinutes(1), expiresAt);
        BlobSasPermission permissions = new BlobSasPermission()
                .setCreatePermission(true)
                .setWritePermission(true);
        BlobServiceSasSignatureValues signatureValues = new BlobServiceSasSignatureValues(expiresAt, permissions)
                .setStartTime(now.minusMinutes(1))
                .setProtocol(SasProtocol.HTTPS_ONLY)
                .setContainerName(containerName)
                .setBlobName(blobName);

        String sas = blobClient.generateUserDelegationSas(signatureValues, delegationKey);
        URI uploadUri = URI.create(blobClient.getBlobUrl() + "?" + sas);
        return new UploadAuthorization(uploadUri.toString(), blobName, expiresAt.toString());
    }

    private void validateRequest(UploadRequest request) {
        if (request == null) {
            throw new IllegalArgumentException("The upload request is required.");
        }
        if (request.fileName() == null || request.fileName().isBlank() || request.fileName().length() > 255) {
            throw new IllegalArgumentException("FileName is required and must be at most 255 characters.");
        }
        if (request.contentType() == null || request.contentType().isBlank() || request.contentType().length() > 127) {
            throw new IllegalArgumentException("ContentType is required and must be at most 127 characters.");
        }
        if (request.fileSizeBytes() < 1 || request.fileSizeBytes() > MAX_CONFIGURED_UPLOAD_SIZE) {
            throw new IllegalArgumentException("FileSizeBytes must be between 1 byte and 5 GiB.");
        }
        if (request.fileSizeBytes() > maxUploadSizeBytes) {
            throw new IllegalArgumentException("The requested file exceeds the configured upload limit.");
        }
        if (!ALLOWED_EXTENSIONS.contains(extensionOf(request.fileName()))) {
            throw new IllegalArgumentException("The file extension is not allowed.");
        }
        if (ALLOWED_CONTENT_TYPES.stream().noneMatch(type -> type.equalsIgnoreCase(request.contentType()))) {
            throw new IllegalArgumentException("The content type is not allowed.");
        }
    }

    private static String extensionOf(String fileName) {
        int separator = Math.max(fileName.lastIndexOf('/'), fileName.lastIndexOf('\\'));
        int dot = fileName.lastIndexOf('.');
        return dot > separator ? fileName.substring(dot).toLowerCase(Locale.ROOT) : "";
    }

    private static String requiredEnvironment(String name) {
        String value = System.getenv(name);
        if (value == null || value.isBlank()) {
            throw new IllegalArgumentException(name + " is required.");
        }
        return value;
    }

    private static String environment(String name, String defaultValue) {
        String value = System.getenv(name);
        return value == null || value.isBlank() ? defaultValue : value;
    }

    private static int integerEnvironment(String name, int defaultValue) {
        String value = System.getenv(name);
        return value == null || value.isBlank() ? defaultValue : Integer.parseInt(value);
    }

    private static long longEnvironment(String name, long defaultValue) {
        String value = System.getenv(name);
        return value == null || value.isBlank() ? defaultValue : Long.parseLong(value);
    }
}
