package com.example.sasupload;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpServer;

import java.io.IOException;
import java.net.InetAddress;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Map;
import java.util.concurrent.ArrayBlockingQueue;
import java.util.concurrent.ThreadPoolExecutor;
import java.util.concurrent.TimeUnit;

public final class ApiServer {
    private static final String ENDPOINT = "/api/uploads/authorize";
    private static final int MAX_REQUEST_BYTES = 16 * 1024;
    private final String apiKey;
    private final UploadSasService uploadSasService;
    private final ObjectMapper objectMapper = new ObjectMapper()
            .disable(com.fasterxml.jackson.databind.DeserializationFeature.ACCEPT_FLOAT_AS_INT);

    public ApiServer() {
        apiKey = requiredEnvironment("ApiSecurity__ApiKey");
        uploadSasService = new UploadSasService();
    }

    public void start() throws IOException {
        int port = Integer.parseInt(System.getenv().getOrDefault("SAS_UPLOAD_API_PORT", "5018"));
        HttpServer server = HttpServer.create(
                new InetSocketAddress(InetAddress.getLoopbackAddress(), port),
                0);
        server.setExecutor(new ThreadPoolExecutor(
                4,
                16,
                60,
                TimeUnit.SECONDS,
                new ArrayBlockingQueue<>(128),
                new ThreadPoolExecutor.CallerRunsPolicy()));
        server.createContext(ENDPOINT, this::handleAuthorization);
        server.start();
        System.out.println("Java upload API listening on http://localhost:" + port);
    }

    private void handleAuthorization(HttpExchange exchange) throws IOException {
        try (exchange) {
            if (!"POST".equals(exchange.getRequestMethod())
                    || !ENDPOINT.equals(exchange.getRequestURI().getPath())) {
                sendJson(exchange, 404, Map.of("title", "Not found", "status", 404));
                return;
            }

            var apiKeys = exchange.getRequestHeaders().get("X-Api-Key");
            if (apiKeys == null || apiKeys.size() != 1 || !apiKeyMatches(apiKeys.get(0))) {
                sendJson(exchange, 401, Map.of("title", "Unauthorized", "status", 401));
                return;
            }

            byte[] body = exchange.getRequestBody().readNBytes(MAX_REQUEST_BYTES + 1);
            if (body.length > MAX_REQUEST_BYTES) {
                sendJson(exchange, 413, Map.of("title", "Request too large", "status", 413));
                return;
            }

            try {
                UploadRequest request = objectMapper.readValue(body, UploadRequest.class);
                sendJson(exchange, 200, uploadSasService.authorize(request));
            } catch (IllegalArgumentException exception) {
                sendJson(exchange, 400, Map.of(
                        "title", "Invalid upload request",
                        "status", 400,
                        "detail", "The upload metadata is not allowed by this service."));
            } catch (IOException exception) {
                sendJson(exchange, 400, Map.of("title", "Invalid JSON request", "status", 400));
            } catch (RuntimeException exception) {
                System.err.println("Could not issue upload authorization: " + exception.getMessage());
                sendJson(exchange, 500, Map.of("title", "Internal server error", "status", 500));
            }
        }
    }

    private boolean apiKeyMatches(String suppliedKey) {
        if (suppliedKey == null || suppliedKey.isBlank()) {
            return false;
        }
        try {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            byte[] expected = digest.digest(apiKey.getBytes(StandardCharsets.UTF_8));
            byte[] supplied = digest.digest(suppliedKey.getBytes(StandardCharsets.UTF_8));
            return MessageDigest.isEqual(expected, supplied);
        } catch (NoSuchAlgorithmException exception) {
            throw new IllegalStateException(exception);
        }
    }

    private void sendJson(HttpExchange exchange, int status, Object body) throws IOException {
        byte[] response = objectMapper.writeValueAsBytes(body);
        exchange.getResponseHeaders().set("Content-Type", "application/json; charset=utf-8");
        exchange.getResponseHeaders().set("Cache-Control", "no-store");
        exchange.sendResponseHeaders(status, response.length);
        exchange.getResponseBody().write(response);
    }

    private static String requiredEnvironment(String name) {
        String value = System.getenv(name);
        if (value == null || value.isBlank()) {
            throw new IllegalArgumentException(name + " is required.");
        }
        return value;
    }
}
