using System;
using System.Threading;
using System.Threading.Tasks;
using ImageMagick;

namespace DualView.GUI.Models;

/// <summary>
///   An interface to show a visual media resource in the GUI. This instance can be displayed by one viewer at a time
///   due to the way animation decoding works.
/// </summary>
public interface IVisualMediaSource : IDisposable
{
    public enum LoadType
    {
        None,
        FullSize,
        Thumbnail,
    }

    public bool IsAnimated { get; }
    public bool HasAudio { get; }
    public bool PlayAudio { get; set; }

    public LoadType LoadStatus { get; }

    public Task RequestLoadFull();
    public Task RequestLoadThumbnail();

    public MagickImage GetCurrentFrame();

    public Task<MagickImage> GetCurrentFrameAsync(CancellationToken token);

    public TimeSpan GetNextFrameTime();
    public void AdvanceFrame();

    public Task AdvanceFrameAsync(CancellationToken token);

    /// <summary>
    ///   Get the name of the media source (this is async to support server fetch)
    /// </summary>
    /// <returns>Name of this (in rare cases just a generic string)</returns>
    public Task<string> GetName();

    public IVisualMediaSource Clone();
}
