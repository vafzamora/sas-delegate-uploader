using SasUpload.Client;

if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Length == 0)
{
    Console.WriteLine(UploadCommand.Usage);
    return;
}

try
{
    var command = UploadCommand.Parse(args);
    await command.RunAsync();
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(UploadCommand.Usage);
    Environment.ExitCode = 2;
}
catch (HttpRequestException exception)
{
    Console.Error.WriteLine($"HTTP request failed: {exception.Message}");
    Environment.ExitCode = 1;
}
catch (IOException exception)
{
    Console.Error.WriteLine($"File operation failed: {exception.Message}");
    Environment.ExitCode = 1;
}
