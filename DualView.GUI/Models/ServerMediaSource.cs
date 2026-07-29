using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;
using ImageMagick;

namespace DualView.GUI.Models;

public class ServerMediaSource : BaseMediaSource, IVisualMediaSource
{
    /// <summary>
    ///   Shared HTTP client so that each image in a list doesn't need its own client
    /// </summary>
    protected static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(120),
    };

    protected readonly IConfiguredMediaInfo MediaInfo;

    public IConfiguredMediaInfo Info => MediaInfo;

    public ServerMediaSource(IConfiguredMediaInfo mediaInfo, IServiceProvider videoPlayerServiceProvider) : base(
        videoPlayerServiceProvider)
    {
        MediaInfo = mediaInfo;
    }

    public long ServerId => MediaInfo.Id;

    /// <summary>
    ///   Called during app startup to set the base URL for the HTTP client
    /// </summary>
    /// <param name="baseUrl">Base URL from the GUI config</param>
    public static void SetupHttpClient(Uri baseUrl)
    {
        HttpClient.BaseAddress = baseUrl;
    }

    public static async Task<(Stream Data, long ExpectedLength)> DownloadFullMedia(long mediaConfigId)
    {
        var response = await HttpClient.GetAsync($"api/v1/MediaContent/{mediaConfigId}/content",
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var expectedLength = response.Content.Headers.ContentLength ?? 0;

        return (await response.Content.ReadAsStreamAsync(), expectedLength);
    }

    public static async Task DownloadFullMediaToLocalFile(long mediaConfigId, string targetFile)
    {
        var (reader, length) = await DownloadFullMedia(mediaConfigId);
        await using var stream = reader;

        var path = Path.GetDirectoryName(targetFile);

        if (!string.IsNullOrEmpty(path) && !Directory.Exists(path))
            Directory.CreateDirectory(path);

        await using (var file = File.Create(targetFile))
        {
            await stream.CopyToAsync(file);
        }

        if (new FileInfo(targetFile).Length != length)
        {
            throw new Exception("Failed to read full file");
        }
    }

    public async Task RequestLoadFull()
    {
        await LoadActionLock.WaitAsync();

        try
        {
            if (LoadStatus == IVisualMediaSource.LoadType.FullSize)
                return;

            PrepareForNewMedia();

            var imageRequest = await SendRequest(GetFullDownloadUrl());
            imageRequest.EnsureSuccessStatusCode();

            if (MediaInfo.MediaType.IsImage())
            {
                await using var data = await imageRequest.Content.ReadAsStreamAsync().ConfigureAwait(false);

                if (MediaInfo.MediaType.IsAnimated())
                {
                    var collection = new MagickImageCollection();
                    await collection.ReadAsync(data).ConfigureAwait(false);

                    // TODO: should this be done in a task in case this takes a while?
                    foreach (var frame in collection)
                    {
                        frame.AutoOrient();
                    }

                    LoadImage(null, collection);
                }
                else
                {
                    var image = new MagickImage();
                    await image.ReadAsync(data).ConfigureAwait(false);

                    image.AutoOrient();

                    LoadImage(image, null);
                }
            }
            else
            {
                var memoryStream = new MemoryStream();
                if (imageRequest.Content.Headers.ContentLength != null)
                    memoryStream.Capacity = (int)imageRequest.Content.Headers.ContentLength.Value;

                await imageRequest.Content.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                // This will dispose of the memory when not needed any more
                await StartVideo(memoryStream);
            }

            LoadStatus = IVisualMediaSource.LoadType.FullSize;
        }
        finally
        {
            LoadActionLock.Release();
        }
    }

    public async Task RequestLoadThumbnail()
    {
        await LoadActionLock.WaitAsync();

        try
        {
            if (LoadStatus == IVisualMediaSource.LoadType.Thumbnail)
                return;

            PrepareForNewMedia();

            var imageRequest = await SendRequest(GetThumbnailDownloadUrl());
            imageRequest.EnsureSuccessStatusCode();

            if (MediaInfo.MediaType.IsImage())
            {
                await using var data = await imageRequest.Content.ReadAsStreamAsync().ConfigureAwait(false);

                if (MediaInfo.MediaType.IsAnimated())
                {
                    var collection = new MagickImageCollection();
                    await collection.ReadAsync(data).ConfigureAwait(false);

                    // The server auto-orients thumbnails, so we don't need to apply that here

                    LoadImage(null, collection);
                }
                else
                {
                    var image = new MagickImage();
                    await image.ReadAsync(data).ConfigureAwait(false);
                    LoadImage(image, null);
                }
            }
            else
            {
                // TODO: this somehow crashes when waiting for server to send thumbnails (and maybe we get a folder update
                // at the same time). Maybe we need to wait on dispose for video playback to start before stopping it?
                var memoryStream = new MemoryStream();
                if (imageRequest.Content.Headers.ContentLength != null)
                    memoryStream.Capacity = (int)imageRequest.Content.Headers.ContentLength.Value;

                await imageRequest.Content.CopyToAsync(memoryStream);
                memoryStream.Position = 0;

                await StartVideo(memoryStream, true);
            }

            LoadStatus = IVisualMediaSource.LoadType.Thumbnail;
        }
        finally
        {
            LoadActionLock.Release();
        }
    }

    public virtual string GetFullDownloadUrl()
    {
        return $"api/v1/MediaContent/{MediaInfo.Id}/content";
    }

    public virtual string GetThumbnailDownloadUrl()
    {
        return $"api/v1/MediaContent/{MediaInfo.Id}/thumbnail";
    }

    public Task<string> GetName()
    {
        // This uses the info, so this is a potentially truncated name, but 100 characters should be enough
        return Task.FromResult(MediaInfo.Name);
    }

    public virtual IVisualMediaSource Clone()
    {
        return new ServerMediaSource(MediaInfo, VideoPlayerServiceProvider);
    }

    protected virtual Task<HttpResponseMessage> SendRequest(string url)
    {
        return HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
        }
    }
}
