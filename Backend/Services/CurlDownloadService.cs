using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

/// <summary>
///   Downloads remote files using the cURL command line program.
/// </summary>
public sealed class CurlDownloadService
{
    private const int MaximumLoggedOutputLength = 500;

    private readonly ILogger<CurlDownloadService> logger;
    private readonly ITemporaryFolderService temporaryFolderService;

    public CurlDownloadService(ILogger<CurlDownloadService> logger,
        ITemporaryFolderService temporaryFolderService)
    {
        this.logger = logger;
        this.temporaryFolderService = temporaryFolderService;
    }

    public async Task<string> DownloadAsync(string url, IEnumerable<KeyValuePair<string, string>> headers,
        string? referrer, IReadOnlyDictionary<string, string> cookies, CancellationToken cancellationToken)
    {
        var temporaryFolder = await temporaryFolderService.GetTemporaryFolder();
        var outputPath = Path.Combine(temporaryFolder, Guid.NewGuid().ToString());

        var processStartInfo = new ProcessStartInfo("curl")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        processStartInfo.ArgumentList.Add("--location");
        processStartInfo.ArgumentList.Add("--compressed");
        processStartInfo.ArgumentList.Add("--fail-with-body");
        processStartInfo.ArgumentList.Add("--no-progress-meter");

        foreach (var header in headers)
        {
            processStartInfo.ArgumentList.Add("--header");
            processStartInfo.ArgumentList.Add($"{header.Key}: {header.Value}");
        }

        if (!string.IsNullOrWhiteSpace(referrer))
        {
            processStartInfo.ArgumentList.Add("--referer");
            processStartInfo.ArgumentList.Add(referrer);
        }

        if (cookies.Count > 0)
        {
            var cookieHeader = string.Join("; ", cookies.Select(cookie =>
                $"{cookie.Key}={cookie.Value}"));
            processStartInfo.ArgumentList.Add("--header");
            processStartInfo.ArgumentList.Add($"Cookie: {cookieHeader}");
        }

        processStartInfo.ArgumentList.Add("--output");
        processStartInfo.ArgumentList.Add(outputPath);
        processStartInfo.ArgumentList.Add(url);

        using var process = Process.Start(processStartInfo) ??
                            throw new InvalidOperationException("Failed to start the 'curl' process");

        var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            var standardOutput = await standardOutputTask;
            var standardError = await standardErrorTask;
            var loggedOutput = LimitLoggedOutput(standardOutput);
            var loggedError = LimitLoggedOutput(standardError);

            if (process.ExitCode != 0)
            {
                logger.LogWarning(
                    "cURL download failed with exit code {ExitCode} for {Url}. Output: {Output}. Error: {Error}",
                    process.ExitCode, url, loggedOutput, loggedError);
                throw new InvalidOperationException($"cURL exited with code {process.ExitCode}: {loggedError}");
            }

            logger.LogDebug("cURL download succeeded for {Url}. Output: {Output}. Error: {Error}",
                url, loggedOutput, loggedError);

            if (!File.Exists(outputPath))
                throw new InvalidOperationException("cURL completed without creating an output file");

            return outputPath;
        }
        catch
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // The process exited between HasExited and Kill.
                }

                try
                {
                    await process.WaitForExitAsync(CancellationToken.None);
                }
                catch (InvalidOperationException)
                {
                    // The process exited while it was being terminated.
                }
            }

            try
            {
                File.Delete(outputPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete failed cURL download file {Path}", outputPath);
            }

            throw;
        }
    }

    private static string LimitLoggedOutput(string output)
    {
        return output.Length <= MaximumLoggedOutputLength
            ? output
            : output[..MaximumLoggedOutputLength];
    }
}
