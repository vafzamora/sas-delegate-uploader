package com.example.sasupload;

public final class Main {
    private Main() {
    }

    public static void main(String[] args) throws Exception {
        if (args.length == 0 || "--help".equals(args[0])) {
            System.out.println("Run server: mvn exec:java -Dexec.mainClass=com.example.sasupload.Main -Dexec.args=\"server\"");
            System.out.println("Run client: mvn exec:java -Dexec.mainClass=com.example.sasupload.Main -Dexec.args=\"upload --file <path>\"");
            return;
        }
        switch (args[0]) {
            case "server" -> new ApiServer().start();
            case "upload" -> UploadClient.run(java.util.Arrays.copyOfRange(args, 1, args.length));
            default -> throw new IllegalArgumentException("Expected 'server' or 'upload'.");
        }
    }
}
