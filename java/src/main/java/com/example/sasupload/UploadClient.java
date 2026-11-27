package com.example.sasupload;

import com.azure.storage.blob.BlobClient;
import com.azure.storage.blob.BlobClientBuilder;
import com.azure.storage.blob.models.BlobHttpHeaders;
import com.azure.storage.blob.models.BlobRequestConditions;
import com.azure.storage.blob.options.BlobParallelUploadOptions;
import com.fasterxml.jackson.databind.ObjectMapper;

import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.Instant;
import java.util.Locale;
import java.util.Map;

public final class UploadClient {
    private static final ObjectMapper OBJECT_MAPPER = new ObjectMapper();
    private static final String USAGE =
            "Usage: mvn exec:java -Dexec.mainClass=com.example.sasupload.Main -Dexec.args=\"upload --file <path> [--api-url <url>]\"";

    private UploadClient() {
    }

    public static void run(String[] args) throws IOException, InterruptedException {
        String filePath = option(args, "--file");
        if (filePath == null) {
            throw new IllegalArgumentException("--file is required.\n" + USAGE);
        }
        Path file = Path.of(filePath);
        if (!Files.isRegularFile(file)) {
            throw new IllegalArgumentException("File does not exist: " + filePath);
        }
        String apiUrl = option(args, "--api-url");
        if (apiUrl == null) {
            apiUrl = System.getenv().getOrDefault("SAS_UPLOAD_API_URL", "http://localhost:5018");
        }
        String apiKey = System.getenv("SAS_UPLOAD_API_KEY");
        if (apiKey == null || apiKey.isBlank()) {
            throw new IllegalArgumentException("Set SAS_UPLOAD_API_KEY before running the client.\n" + USAGE);
        }
        URI apiUri = URI.create(apiUrl);
        if (!apiUri.isAbsolute() || !("http".equalsIgnoreCase(apiUri.getScheme())
                || "https".equalsIgnoreCase(apiUri.getScheme()))) {
            throw new IllegalArgumentException("API URL must be an absolute HTTP or HTTPS URL.");
        }

        String contentType = contentType(file);
        UploadRequest metadata = new UploadRequest(file.getFileName().toString(), contentType, Files.size(file));
        String requestBody = OBJECT_MAPPER.writeValueAsString(metadata);
        HttpRequest request = HttpRequest.newBuilder(apiUri.resolve("/api/uploads/authorize"))
                .header("X-Api-Key", apiKey)
                .header("Accept", "application/json")
                .header("Content-Type", "application/json")
                .POST(HttpRequest.BodyPublishers.ofString(requestBody))
                .build();

        HttpResponse<String> response = HttpClient.newHttpClient()
                .send(request, HttpResponse.BodyHandlers.ofString());
        if (response.statusCode() < 200 || response.statusCode() >= 300) {
            throw new IOException("Authorization API returned HTTP " + response.statusCode() + ".");
        }
        UploadAuthorization authorization = OBJECT_MAPPER.readValue(response.body(), UploadAuthorization.class);
        if (!Instant.parse(authorization.expiresAt()).isAfter(Instant.now())) {
            throw new IOException("The upload authorization is already expired.");
        }

        BlobClient blobClient = new BlobClientBuilder()
                .endpoint(authorization.uploadUrl())
                .buildClient();
        try (var input = Files.newInputStream(file)) {
            BlobParallelUploadOptions uploadOptions = new BlobParallelUploadOptions(input)
                    .setHeaders(new BlobHttpHeaders().setContentType(contentType))
                    .setRequestConditions(new BlobRequestConditions().setIfNoneMatch("*"));
            blobClient.uploadWithResponse(uploadOptions, null, null);
        }
        System.out.println("Upload completed: " + authorization.blobName());
        System.out.println("SAS expiration: " + authorization.expiresAt());
    }

    private static String option(String[] args, String name) {
        for (int index = 0; index < args.length; index++) {
            if (name.equalsIgnoreCase(args[index])) {
                if (index + 1 >= args.length || args[index + 1].startsWith("--")) {
                    throw new IllegalArgumentException(name + " requires a value.");
                }
                return args[index + 1];
            }
        }
        return null;
    }

    private static String contentType(Path file) {
        String extension = extension(file).toLowerCase(Locale.ROOT);
        return switch (extension) {
            case ".txt" -> "text/plain";
            case ".pdf" -> "application/pdf";
            case ".png" -> "image/png";
            case ".jpg", ".jpeg" -> "image/jpeg";
            case ".json" -> "application/json";
            case ".zip" -> "application/zip";
            default -> "application/octet-stream";
        };
    }

    private static String extension(Path file) {
        String name = file.getFileName().toString();
        int dot = name.lastIndexOf('.');
        return dot < 0 ? "" : name.substring(dot);
    }
}
