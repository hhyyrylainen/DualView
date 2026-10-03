using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AsyncKeyedLock;
using Backend.Models;
using ImageMagick;
using DualView.Shared.Models.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Services;

/// <summary>
///   Stores remote gallery media separately from the managed media library until the user imports it.
/// </summary>
public sealed class RemoteGalleryMediaService : IRemoteGalleryMediaService
{
    private const int DownloadAttempts = 5;
    private const long MinimumDownloadedFileSize = 100;

    private readonly ILogger<RemoteGalleryMediaService> logger;
    private readonly IServiceScopeFactory serviceScopeFactory;
    private readonly IDataFolderService dataFolderService;
    private readonly ICurlDownloadService curlDownloadService;
    private readonly HttpClient httpClient;
    private readonly AsyncKeyedLocker<string> spriteSheetLocks = new();

    public RemoteGalleryMediaService(ILogger<RemoteGalleryMediaService> logger,
        IServiceScopeFactory serviceScopeFactory, IDataFolderService dataFolderService,
        ICurlDownloadService curlDownloadService)
    {
        this.logger = logger;
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

        httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5),
        };
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
        var database = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var mediaProcessingService = scope.ServiceProvider.GetRequiredService<IMediaProcessingService>();
        var item = await database.GetFoundMediaAsync(itemId) ??
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

        var extension = GetSafeExtension(sourceUrl, item.MediaType?.ExtensionFromType() ?? ".jpg");
        var destination = Path.Combine(directory, thumbnail ? "thumbnail.webp" : "content" + extension);

        var settings = await database.GetAppSettingsAsync();

        var cookies = DeserializeDictionary(item.Cookies);
        var headers = DeserializeDictionary(item.ImpersonationHeaders);
        var referrer = Uri.TryCreate(item.Referrer, UriKind.Absolute, out var parsedReferrer)
            ? parsedReferrer.ToString()
            : null;

        for (var attempt = 1; attempt <= DownloadAttempts; ++attempt)
        {
            try
            {
                if (thumbnail && TryGetThumbnailCrop(item, out var crop))
                {
                    var spriteSheetPath = await GetSpriteSheetPathAsync(sourceUrl, settings.UseCurlForRemoteDownloads,
                        headers, referrer, cookies, cancellationToken);
                    await CreateCroppedThumbnailAsync(spriteSheetPath, destination, crop, cancellationToken);
                }
                else
                {
                    await DownloadFileAsync(sourceUrl, destination, settings.UseCurlForRemoteDownloads, headers,
                        referrer, cookies, cancellationToken);

                    if (thumbnail)
                    {
                        if (IsVideo(sourceUrl, item.MediaType))
                        {
                            var videoThumbnail = Path.Combine(directory, "thumbnail.webm");
                            await mediaProcessingService.ResizeVideoThumbnailAsync(destination, videoThumbnail,
                                cancellationToken);
                            File.Delete(destination);
                            destination = videoThumbnail;
                        }
                        else
                        {
                            await ResizeThumbnailAsync(destination, cancellationToken);
                        }
                    }
                }

                if (thumbnail)
                {
                    item.LocalThumbnailFilePath = destination;
                }
                else
                {
                    item.LocalFullFilePath = destination;
                }

                logger.LogInformation("Successfully downloaded remote gallery item");
                await database.SaveFoundMediaAsync(item);
                return destination;
            }
            catch when (attempt < DownloadAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
        }

        throw new InvalidOperationException("Remote media download exhausted its retry budget");
    }

    private async Task<string> GetSpriteSheetPathAsync(string sourceUrl, bool useCurl,
        Dictionary<string, string> headers, string? referrer, Dictionary<string, string> cookies,
        CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceUrl))).ToLowerInvariant();
        var directory = Path.Combine(dataFolderService.GetDataFolderPath(), "remoteThumbnails");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, hash + GetSafeExtension(sourceUrl, ".img"));
        using var spriteSheetLock = await spriteSheetLocks.LockAsync(path, cancellationToken);
        if (File.Exists(path))
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return path;
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await DownloadFileAsync(sourceUrl, temporaryPath, useCurl, headers, referrer, cookies,
                cancellationToken);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }

        return path;
    }

    private async Task DownloadFileAsync(string sourceUrl, string destination, bool useCurl,
        Dictionary<string, string> headers, string? referrer, Dictionary<string, string> cookies,
        CancellationToken cancellationToken)
    {
        if (useCurl)
        {
            var temporaryPath = await curlDownloadService.DownloadAsync(sourceUrl,
                new BrowserImpersonationHeaders(headers).PassedHeaders(), referrer, cookies, cancellationToken);
            try
            {
                await using var input = File.OpenRead(temporaryPath);
                await using var output = File.Create(destination);
                await input.CopyToAsync(output, cancellationToken);
                EnsureMinimumDownloadedFileSize(output.Length);
            }
            finally
            {
                File.Delete(temporaryPath);
            }

            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, sourceUrl);
        new BrowserImpersonationHeaders(headers).ConfigureHttpRequest(request, false);
        if (referrer != null)
            request.Headers.Referrer = new Uri(referrer);

        if (cookies.Count > 0)
        {
            request.Headers.TryAddWithoutValidation("Cookie",
                string.Join("; ", cookies.Select(cookie => $"{cookie.Key}={cookie.Value}")));
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var outputStream = File.Create(destination);
        await responseStream.CopyToAsync(outputStream, cancellationToken);
        EnsureMinimumDownloadedFileSize(outputStream.Length);
    }

    private static void EnsureMinimumDownloadedFileSize(long fileSize)
    {
        if (fileSize < MinimumDownloadedFileSize)
            throw new InvalidDataException($"Downloaded remote media is too small ({fileSize} bytes)");
    }

    private static bool TryGetThumbnailCrop(FoundMedia item, out DualView.Shared.Models.RemoteThumbnailCrop crop)
    {
        crop = new DualView.Shared.Models.RemoteThumbnailCrop();
        if (!item.ThumbnailCropX.HasValue || !item.ThumbnailCropY.HasValue ||
            !item.ThumbnailCropWidth.HasValue || !item.ThumbnailCropHeight.HasValue ||
            item.ThumbnailCropX.Value < 0 || item.ThumbnailCropY.Value < 0 ||
            item.ThumbnailCropWidth.Value <= 0 || item.ThumbnailCropHeight.Value <= 0)
        {
            return false;
        }

        crop.X = item.ThumbnailCropX.Value;
        crop.Y = item.ThumbnailCropY.Value;
        crop.Width = item.ThumbnailCropWidth.Value;
        crop.Height = item.ThumbnailCropHeight.Value;
        return true;
    }

    private static async Task CreateCroppedThumbnailAsync(string spriteSheetPath, string destination,
        DualView.Shared.Models.RemoteThumbnailCrop crop, CancellationToken cancellationToken)
    {
        using var images = new MagickImageCollection();
        await using (var input = File.OpenRead(spriteSheetPath))
        {
            await images.ReadAsync(input, cancellationToken);
        }

        if (images.Count == 0)
            throw new InvalidOperationException("Downloaded sprite sheet contains no image frames");

        images.Coalesce();
        foreach (var image in images)
        {
            image.Crop(new MagickGeometry(crop.X, crop.Y, (uint)crop.Width, (uint)crop.Height));
            image.AutoOrient();
            MediaProcessingService.ResizeWithDivisibleByTwoDimensions(image);
            image.Strip();
        }

        await images.WriteAsync(destination, MagickFormat.WebP, cancellationToken);
    }

    private static async Task ResizeThumbnailAsync(string path, CancellationToken cancellationToken)
    {
        using var images = new MagickImageCollection();

        // Read any supported file type without caring about the format.
        await using (var input = File.OpenRead(path))
        {
            await images.ReadAsync(input, cancellationToken);
        }

        if (images.Count == 0)
            throw new InvalidOperationException("Downloaded thumbnail contains no image frames");
        images.Coalesce();
        foreach (var image in images)
        {
            image.AutoOrient();
            MediaProcessingService.ResizeWithDivisibleByTwoDimensions(image);
            image.Strip();
        }

        await images.WriteAsync(path, MagickFormat.WebP, cancellationToken);
    }

    private static Dictionary<string, string> DeserializeDictionary(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : JsonSerializer.Deserialize<Dictionary<string, string>>(value) ?? new Dictionary<string, string>();
    }

    private static string GetSafeExtension(string url, string fallback)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
               Path.GetExtension(uri.AbsolutePath) is { Length: > 0 } extension && extension.Length <= 10
            ? extension
            : fallback;
    }

    private static bool IsVideo(string url, MediaType? mediaType)
    {
        if (mediaType.HasValue)
            return !mediaType.Value.IsImage();

        try
        {
            return !MediaTypeExtensions.TypeFromExtension(GetSafeExtension(url, string.Empty)).IsImage();
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
