package com.example.sasupload;

public record UploadRequest(String fileName, String contentType, long fileSizeBytes) {
}
