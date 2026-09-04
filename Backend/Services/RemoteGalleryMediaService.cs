using System.Net;
using System.Text.Json;
using Backend.Database;
using ImageMagick;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Services;

/// <summary>
///   Stores remote gallery media separately from the managed media library until the user imports it.
/// </summary>
public sealed class RemoteGalleryMediaService : IRemoteGalleryMediaService
{
    private const int DownloadAttempts = 5;
    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly IDataFolderService dataFolderService;
    private readonly ICurlDownloadService curlDownloadService;
    private readonly HttpClient httpClient;

    public RemoteGalleryMediaService(IServiceScopeFactory serviceScopeFactory, IDataFolderService dataFolderService,
        ICurlDownloadService curlDownloadService)
    {
        this.serviceScopeFactory = serviceScopeFactory;
        this.dataFolderService = dataFolderService;
        this.curlDownloadService = curlDownloadService;
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(30),
            UseCookies = false,
            SslOptions = { RemoteCertificateValidationCallback = static (_, _, _, _) => true },
        };
        httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15), };
    }

    public Task<string> GetThumbnailAsync(long itemId, CancellationToken cancellationToken)
    {
        return DownloadAsync(itemId, true, cancellationToken);
    }

    public Task<string> GetFullMediaAsync(long itemId, CancellationToken cancellationToken)
    {
        return DownloadAsync(itemId, false, cancellationToken);
    }

    private async Task<string> DownloadAsync(long itemId, bool thumbnail, CancellationToken cancellationToken)
    {
        using var scope = serviceScopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var item = await database.FoundMedia.FindAsync([itemId], cancellationToken) ??
                   throw new ArgumentException("Remote gallery item not found", nameof(itemId));
        var existing = thumbnail ? item.LocalThumbnailFilePath : item.LocalFullFilePath;
        if (!string.IsNullOrWhiteSpace(existing) && File.Exists(existing))
            return existing;
        var sourceUrl = thumbnail && !string.IsNullOrWhiteSpace(item.ThumbnailUrl)
            ? item.ThumbnailUrl
            : item.DownloadUrl;
        var directory = Path.Combine(dataFolderService.GetDataFolderPath(), "remoteMedia", (item.Id % 100).ToString(),
            item.Id.ToString());
        Directory.CreateDirectory(directory);

        var extension = GetSafeExtension(sourceUrl, ".jpg");
        var destination = Path.Combine(directory, thumbnail ? "thumbnail.jpg" : "content" + extension);

        var settings = await database.AppSettings.AsNoTracking().FirstAsync(cancellationToken);

        var cookies = DeserializeDictionary(item.Cookies);
        var headers = DeserializeDictionary(item.ImpersonationHeaders);
        var referrer = Uri.TryCreate(item.Referrer, UriKind.Absolute, out var parsedReferrer)
            ? parsedReferrer.ToString()
            : null;

        for (var attempt = 1; attempt <= DownloadAttempts; ++attempt)
        {
            try
            {
                string? temporaryPath = null;
                try
                {
                    if (settings.UseCurlForRemoteDownloads)
                    {
                        temporaryPath = await curlDownloadService.DownloadAsync(sourceUrl,
                            new BrowserImpersonationHeaders(headers).PassedHeaders(), referrer, cookies,
                            cancellationToken);
                        await using var input = File.OpenRead(temporaryPath);
                        await using var output = File.Create(destination);
                        await input.CopyToAsync(output, cancellationToken);
                    }
                    else
                    {
                        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
                        new BrowserImpersonationHeaders(headers).ConfigureHttpRequest(request, false);
                        if (referrer != null)
                            request.Headers.Referrer = new Uri(referrer);
                        if (cookies.Count > 0)
                            request.Headers.TryAddWithoutValidation("Cookie",
                                string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
                        using var response = await httpClient.SendAsync(request,
                            HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                        response.EnsureSuccessStatusCode();
                        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
                        await using var output = File.Create(destination);
                        await input.CopyToAsync(output, cancellationToken);
                    }
                }
                finally
                {
                    if (temporaryPath != null)
                        File.Delete(temporaryPath);
                }

                if (thumbnail)
                    await ResizeThumbnailAsync(destination, cancellationToken);
                if (thumbnail)
                    item.LocalThumbnailFilePath = destination;
                else
                    item.LocalFullFilePath = destination;
                await database.SaveChangesAsync(cancellationToken);
                return destination;
            }
            catch when (attempt < DownloadAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
        }

        throw new InvalidOperationException("Remote media download exhausted its retry budget");
    }

    private static async Task ResizeThumbnailAsync(string path, CancellationToken cancellationToken)
    {
        using var images = new MagickImageCollection();
        await images.ReadAsync(path, cancellationToken);
        if (images.Count == 0)
            throw new InvalidOperationException("Downloaded thumbnail contains no image frames");
        images.Coalesce();
        foreach (var image in images)
        {
            image.AutoOrient();
            MediaProcessingService.ResizeWithDivisibleByTwoDimensions(image);
            image.Strip();
        }

        await images.WriteAsync(path, MagickFormat.Jpeg, cancellationToken);
    }

    private static Dictionary<string, string> DeserializeDictionary(string? value) => string.IsNullOrWhiteSpace(value)
        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        : JsonSerializer.Deserialize<Dictionary<string, string>>(value) ?? new Dictionary<string, string>();

    private static string GetSafeExtension(string url, string fallback) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        Path.GetExtension(uri.AbsolutePath) is { Length: > 0 } extension && extension.Length <= 10
            ? extension
            : fallback;
}
