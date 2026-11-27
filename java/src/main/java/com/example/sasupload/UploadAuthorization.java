package com.example.sasupload;

public record UploadAuthorization(String uploadUrl, String blobName, String expiresAt) {
}
