using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using DualView.Shared.Models.DTO;

namespace DualView.GUI.Models;

/// <summary>
///   Fetches a preview of an edit from the server. Note that we are supposed to re-create these instances when the
///   data changes, so this is basically the same as normal server media fetch but with an overridden URL endpoint.
/// </summary>
public class ServerEditPreview : ServerMediaSource
{
    public ServerEditPreview(IConfiguredMediaInfo mediaInfo, IServiceProvider videoPlayerServiceProvider) : base(
        mediaInfo, videoPlayerServiceProvider)
    {
    }

    public override string GetFullDownloadUrl()
    {
        return $"api/v1/Media/{MediaInfo.Id}/previewChanges";
    }

    public override string GetThumbnailDownloadUrl()
    {
        throw new NotSupportedException("Edit previews do not have thumbnails");
    }

    public override IVisualMediaSource Clone()
    {
        return new ServerEditPreview(MediaInfo, VideoPlayerServiceProvider);
    }

    protected override Task<HttpResponseMessage> SendRequest(string url, bool smallRequest)
    {
        // We need to send it as the real type, otherwise not all properties are written
        var data = JsonSerializer.Serialize(MediaInfo, MediaInfo.GetType(), JsonSerializerOptions.Web);

        return HttpClient.PostAsync(url, new StringContent(data, System.Text.Encoding.UTF8, "application/json"));
    }
}
