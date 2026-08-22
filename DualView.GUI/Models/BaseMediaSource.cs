using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DualView.GUI.Services;
using ImageMagick;
using ImageMagick.Drawing;
using Microsoft.Extensions.DependencyInjection;

namespace DualView.GUI.Models;

public abstract class BaseMediaSource(IServiceProvider serviceProvider) : IDisposable
{
    protected readonly IServiceProvider VideoPlayerServiceProvider = serviceProvider;

    protected readonly SemaphoreSlim LoadActionLock = new(1, 1);

    /// <summary>
    ///   Limits concurrent media decoding to avoid overwhelming the client when many media sources load together
    /// </summary>
    protected static readonly SemaphoreSlim ImageDecodingLock = new(4, 4);

    protected bool UseAlphaForVideos = true;

    protected MagickImage? CurrentFrame;

    private MagickImageCollection? loadedAnimation;
    private MagickImage? previousCanvasBackup;
    private GifDisposeMethod previousDisposeMethod = GifDisposeMethod.None;
    private MagickGeometry previousGeometry = new();

    private TimeSpan nextFrameTime = TimeSpan.Zero;

    private FfmpegDecoderService.AvFormatWrapper? avFormatWrapper;

    private PlayingStatus videoPausedStatus;

    private int currentAnimationFrame;

    private int audioStreamId = Random.Shared.Next();

    private bool hasNextVideoFrame;

    private enum PlayingStatus
    {
        Playing,
        Paused,
        Stopped,
    };

    public bool IsAnimated { get; protected set; }

    public bool HasAudio { get; protected set; }

    public bool PlayAudio { get; set; }

    public IVisualMediaSource.LoadType LoadStatus { get; protected set; }

    public TimeSpan GetNextFrameTime()
    {
        if (!IsAnimated)
            throw new InvalidOperationException("This is not animated");

        return nextFrameTime;
    }

    public void AdvanceFrame()
    {
        LoadActionLock.Wait();
        try
        {
            AdvanceFrameInternal();
        }
        finally
        {
            LoadActionLock.Release();
        }
    }

    public async Task AdvanceFrameAsync(CancellationToken token)
    {
        await LoadActionLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            await AdvanceFrameInternalAsync(token).ConfigureAwait(false);
        }
        finally
        {
            LoadActionLock.Release();
        }
    }

    private void AdvanceFrameInternal()
    {
        if (CurrentFrame == null)
            return;

        if (avFormatWrapper != null)
        {
            if (!avFormatWrapper.ReadUntilNextFrame())
            {
                // Hopefully reached the end here, so restart the video
                RestartVideo();
                return;
            }

            // We got a frame
            hasNextVideoFrame = true;
            return;
        }

        if (loadedAnimation != null)
        {
            ++currentAnimationFrame;
            if (currentAnimationFrame >= loadedAnimation.Count)
            {
                RestartGifAnimation();
            }
            else
            {
                ApplyGifToBuffer(CurrentFrame, loadedAnimation[currentAnimationFrame]);

                SetNextGifFrameDelay(loadedAnimation[currentAnimationFrame]);
            }

            return;
        }

        throw new InvalidOperationException("Cannot advance frame as not animated");
    }

    private async Task AdvanceFrameInternalAsync(CancellationToken token)
    {
        if (CurrentFrame == null)
            return;

        if (avFormatWrapper != null)
        {
            if (!await avFormatWrapper.ReadUntilNextFrameAsync(token).ConfigureAwait(false))
            {
                // Hopefully reached the end here, so restart the video
                await RestartVideoInternalAsync(token).ConfigureAwait(false);
                return;
            }

            // We got a frame
            hasNextVideoFrame = true;
            return;
        }

        if (loadedAnimation != null)
        {
            ++currentAnimationFrame;
            if (currentAnimationFrame >= loadedAnimation.Count)
            {
                RestartGifAnimation();
            }
            else
            {
                ApplyGifToBuffer(CurrentFrame, loadedAnimation[currentAnimationFrame]);

                SetNextGifFrameDelay(loadedAnimation[currentAnimationFrame]);
            }

            return;
        }

        throw new InvalidOperationException("Cannot advance frame as not animated");
    }

    // TODO: we should have a more efficient path for video playback where the direct bitmap can be written
    public MagickImage GetCurrentFrame()
    {
        MagickImage? frame;

        // Don't allow someone to dispose of the frame if we are just copying it
        LoadActionLock.Wait();
        try
        {
            frame = GetCurrentFrameInternal();
        }
        finally
        {
            LoadActionLock.Release();
        }

        return frame;
    }

    public async Task<MagickImage> GetCurrentFrameAsync(CancellationToken token)
    {
        MagickImage? frame;

        await LoadActionLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            frame = GetCurrentFrameInternal();
        }
        finally
        {
            LoadActionLock.Release();
        }

        return frame;
    }

    private MagickImage GetCurrentFrameInternal()
    {
        var frame = CurrentFrame;

        if (frame == null)
            throw new InvalidOperationException("Frame not loaded yet");

        if (avFormatWrapper != null)
        {
            if (hasNextVideoFrame)
            {
                avFormatWrapper.DecodeCurrentFrame(frame);

                hasNextVideoFrame = false;

                if (videoPausedStatus != PlayingStatus.Playing)
                {
                    Task.Run(UnPause);
                }
            }
        }

        return frame;
    }

    public void Dispose()
    {
        LoadActionLock.Wait(TimeSpan.FromSeconds(20));

        try
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        finally
        {
            LoadActionLock.Release();
        }
    }

    protected async Task StartVideo(Stream media, bool thumbnail = false)
    {
        PrepareForNewMedia();

        IsAnimated = true;

        if (thumbnail)
        {
            // Prevent audio from eating performance by disabling entirely.
            // media.AddOption(":no-audio");
        }

        // vlcMediaSource = media;

        // For now, we want to mute full videos as well (TODO: add user button to unmute playing)
        await CreateAndStartPlayer(media, true, thumbnail);
    }

    protected async Task RestartVideoInternalAsync(CancellationToken cancellationToken)
    {
        if (avFormatWrapper == null)
            return;

        await avFormatWrapper.RestartVideoAsync(cancellationToken).ConfigureAwait(false);
        VideoPlayerServiceProvider.GetRequiredService<IAudioPlaybackService>().ClearQueue(audioStreamId);
        audioStreamId = Random.Shared.Next();
        videoPausedStatus = PlayingStatus.Playing;
    }

    protected void RestartVideo()
    {
        Task.Run(async () =>
        {
            await LoadActionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await RestartVideoInternalAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to restart video: {e}");
            }
            finally
            {
                LoadActionLock.Release();
            }
        });
    }

    protected void UnPause()
    {
        LoadActionLock.Wait();
        try
        {
            if (avFormatWrapper == null || videoPausedStatus == PlayingStatus.Playing)
                return;

            // TODO: implement pausing support
            throw new NotImplementedException();
            // avFormatWrapper.Play();
            // videoPausedStatus = PlayingStatus.Playing;
        }
        finally
        {
            LoadActionLock.Release();
        }
    }

    protected void StopPlaying()
    {
        LoadActionLock.Wait();
        try
        {
            if (avFormatWrapper == null || videoPausedStatus == PlayingStatus.Stopped)
                return;

            throw new NotImplementedException();
            // avFormatWrapper.Stop();
            // videoPausedStatus = PlayingStatus.Stopped;
        }
        finally
        {
            LoadActionLock.Release();
        }
    }

    protected virtual void StopVideo(bool disposing)
    {
#if DEBUG
        if (LoadActionLock.CurrentCount > 0)
            throw new InvalidOperationException("Load action lock not held");
#endif

        // runVideoCheck = false;
        var player = avFormatWrapper;
        avFormatWrapper = null;

        if (player != null)
        {
            player.OnAudioSamplesDecoded -= OnAudioSamplesDecoded;

            if (!disposing)
                VideoPlayerServiceProvider.GetRequiredService<IAudioPlaybackService>().ClearQueue(audioStreamId);
            player.Dispose();
        }
    }

    protected void PausePlaying()
    {
        LoadActionLock.Wait();
        try
        {
            if (avFormatWrapper == null || videoPausedStatus == PlayingStatus.Paused)
                return;

            // TODO: audio pausing
            videoPausedStatus = PlayingStatus.Paused;
        }
        finally
        {
            LoadActionLock.Release();
        }
    }

    protected virtual void AdjustOutputResolution(ref int width, ref int height)
    {
    }

    protected void LoadImage(MagickImage? singleImage, MagickImageCollection? animated)
    {
        if (singleImage == null && animated == null)
            throw new ArgumentException("Either single image or animated must be provided");

        if (singleImage != null && animated != null)
            throw new ArgumentException("Can't provide both animated and single frame");

        // We must be already locked, so this is safe to do
        PrepareForNewMedia();

        if (singleImage != null)
        {
            CurrentFrame = singleImage;
        }
        else if (animated != null)
        {
            loadedAnimation = animated;

            var firstFrame = loadedAnimation[0];

            // Create an animation buffer based on the first frame
            // If this doesn't have multiple frames, we are wasting one extra image, but hopefully non-animated things
            // are not detected as animated
            CurrentFrame = new MagickImage(MagickColors.Transparent, firstFrame.Width, firstFrame.Height);

            RestartGifAnimation();
        }
        else
        {
            throw new Exception("Logic error in null handling");
        }
    }

    protected void PrepareForNewMedia(bool disposing = false)
    {
#if DEBUG
        if (LoadActionLock.CurrentCount > 0)
            throw new InvalidOperationException("Load action lock not held");
#endif

        LoadStatus = IVisualMediaSource.LoadType.None;
        StopVideo(disposing);

        CurrentFrame?.Dispose();
        CurrentFrame = null;

        loadedAnimation?.Dispose();
        loadedAnimation = null;

        previousCanvasBackup?.Dispose();
        previousCanvasBackup = null;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            LoadStatus = IVisualMediaSource.LoadType.None;

            // This clears current buffers etc.
            PrepareForNewMedia(true);
        }
    }

    private void OnAudioSamplesDecoded(byte[] samples)
    {
        if (PlayAudio)
        {
            VideoPlayerServiceProvider.GetRequiredService<IAudioPlaybackService>().PlaySamples(audioStreamId, samples);
        }
    }

    private static MagickGeometry GetFramePage(IMagickImage frame)
    {
        // GIF frames can have offsets via Page (a.k.a. "virtual canvas").
        // Some encoders leave Page empty/zero-ish; fall back to frame dimensions at (0,0).
        var p = frame.Page;

        var w = p.Width > 0 ? p.Width : frame.Width;
        var h = p.Height > 0 ? p.Height : frame.Height;

        return new MagickGeometry(p.X, p.Y, w, h);
    }

    private static void ClampToCanvas(IMagickGeometry rect, uint canvasW, uint canvasH,
        out int x0, out int y0, out int x1, out int y1)
    {
        // rect uses x/y + width/height; Draw rectangle wants two corners.
        x0 = Math.Clamp(rect.X, 0, (int)canvasW);
        y0 = Math.Clamp(rect.Y, 0, (int)canvasH);

        var rx1 = rect.X + (int)rect.Width;
        var ry1 = rect.Y + (int)rect.Height;

        x1 = Math.Clamp(rx1, 0, (int)canvasW);
        y1 = Math.Clamp(ry1, 0, (int)canvasH);
    }

    private async Task CreateAndStartPlayer(Stream stream, bool mute, bool thumbnail)
    {
        // This can only be called when the load action lock is already locked
#if DEBUG
        if (LoadActionLock.CurrentCount > 0)
            throw new InvalidOperationException("Load action lock not held");
#endif
        if (avFormatWrapper != null)
            throw new InvalidOperationException("Previous player not destroyed");

        avFormatWrapper = VideoPlayerServiceProvider.GetRequiredService<FfmpegDecoderService>().OpenFfmpeg(stream);
        // Doesn't seem to help with video player problems.
        // avFormatWrapper.VideoThreadCount = thumbnail ? 1 : Math.Min(Environment.ProcessorCount, 4);

        var inspectTask = Task.Run(() =>
        {
            if (!avFormatWrapper.FindStreamInfo())
                throw new Exception("Could not find stream info");
        });

        // Wait until the player is ready to play (or decoding the video is likely not going to work)
        try
        {
            await inspectTask.WaitAsync(TimeSpan.FromSeconds(30));

            // After inspecting we need to open the video stream so that we actually know the size etc.
            await Task.Run(() =>
            {
                if (!avFormatWrapper.OpenVideoStream())
                    throw new Exception("Could not open video stream");

                if (avFormatWrapper.OpenAudioStream())
                {
                    HasAudio = true;
                    avFormatWrapper.OnAudioSamplesDecoded += OnAudioSamplesDecoded;
                }
            });
        }
        catch (Exception e)
        {
            var wrapper = avFormatWrapper;
            avFormatWrapper = null;

            // Dispose in the background in case it is permanently stuck
            _ = Task.Run(() =>
            {
                try
                {
                    wrapper?.Dispose();
                }
                catch (Exception e2)
                {
                    // We don't really care if this fails, but try to "log" anyway
                    Console.WriteLine(e2);
                }
            });

            throw new Exception("Could not find stream info or open it for display", e);
        }

        if (avFormatWrapper.GetVideoSize(out var width, out var height))
        {
            AdjustOutputResolution(ref width, ref height);

            // Current frame should be disposed already if it existed, so we just assign here

            if (!UseAlphaForVideos)
            {
                // Assume that videos do not have alpha in them
                CurrentFrame = new MagickImage(MagickColors.Black, (uint)width, (uint)height);
                CurrentFrame.Alpha(AlphaOption.Off);
            }
            else
            {
                // For some reason this seems to be *way*, *way* faster to render than the no alpha variant
                CurrentFrame = new MagickImage(MagickColors.Transparent, (uint)width, (uint)height);
            }
        }
        else
        {
            throw new Exception("Could not get video size");
        }

        if (!avFormatWrapper.GetFrameRate(out var fps))
        {
            // TODO: problem?
            nextFrameTime = TimeSpan.FromMilliseconds(33);
        }
        else
        {
            if (thumbnail)
                fps = Math.Min(fps, 24);

            nextFrameTime = TimeSpan.FromSeconds(1.0 / fps);
        }

        videoPausedStatus = PlayingStatus.Playing;
    }

    private void RestartGifAnimation()
    {
        if (CurrentFrame == null || loadedAnimation == null)
            throw new InvalidOperationException("No animation loaded");

        currentAnimationFrame = 0;
        var firstFrame = loadedAnimation[0];

        // Copy it to the temporary buffer to have the first frame again ready
        // Apparently copy pixels operation doesn't copy all of the pixels...
        CurrentFrame.Composite(firstFrame, CompositeOperator.Copy);

        // Ensure transparency is respected.
        CurrentFrame.Alpha(AlphaOption.On);

        SetNextGifFrameDelay(firstFrame);

        previousDisposeMethod = firstFrame.GifDisposeMethod;
        previousGeometry = GetFramePage(firstFrame);

        IsAnimated = loadedAnimation.Count > 1;
    }

    private void SetNextGifFrameDelay(IMagickImage currentFrame)
    {
        nextFrameTime = TimeSpan.FromMilliseconds(currentFrame.AnimationDelay * 10);
    }

    /// <summary>
    ///   Mutates <paramref name="currentFrame"/> to advance the animation by compositing <paramref name="newFrame"/>.
    ///   Call this once per displayed frame in decoding order.
    /// </summary>
    private void ApplyGifToBuffer(MagickImage currentFrame, IMagickImage<byte> newFrame)
    {
        if (currentFrame == null)
            throw new ArgumentNullException(nameof(currentFrame));
        if (newFrame == null)
            throw new ArgumentNullException(nameof(newFrame));

        var dispose = previousDisposeMethod;
        var rect = previousGeometry;

        // Apply disposal of the previously displayed frame
        if (dispose == GifDisposeMethod.Undefined)
            dispose = GifDisposeMethod.None;

        switch (dispose)
        {
            case GifDisposeMethod.None:
                // Leave pixels as-is.
                break;

            case GifDisposeMethod.Background:
            {
                // Clear the previous frame's rectangle to "background".
                // For GUI work, "transparent" is generally the right interpretation.
                ClampToCanvas(rect, currentFrame.Width, currentFrame.Height, out var x0, out var y0, out var x1,
                    out var y1);
                if (x1 > x0 && y1 > y0)
                {
                    currentFrame.Draw(
                        new DrawableFillColor(MagickColors.Transparent),
                        new DrawableStrokeColor(MagickColors.Transparent),
                        new DrawableRectangle(x0, y0, x1 - 1, y1 - 1)
                    );
                }

                break;
            }

            case GifDisposeMethod.Previous:
            {
                // Restore the canvas to what it was before the previous frame was composited.
                if (previousCanvasBackup != null)
                {
                    // Replace currentFrame pixels with the backup.
                    // Composite Copy is an in-place "copy pixels operation" without changing the object reference.
                    currentFrame.Composite(previousCanvasBackup, CompositeOperator.Copy);
                }

                break;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(dispose), dispose, "Unknown GIF dispose method");
        }

        // 2) If *this* frame asks for "Previous", save the canvas before drawing it.
        // (The restore will happen on the next call, when this frame becomes the "previous displayed frame".)
        var thisDispose = newFrame.GifDisposeMethod;
        if (thisDispose == GifDisposeMethod.Previous)
        {
            // TODO: reusable previous canvas if this is a common operation?
            previousCanvasBackup?.Dispose();
            previousCanvasBackup = (MagickImage)currentFrame.Clone();
        }

        // 3) Composite the new frame onto the canvas at its page offset.
        var page = GetFramePage(newFrame);

        currentFrame.Composite(newFrame, page.X, page.Y, CompositeOperator.Over);

        // Remember the next GIF operation
        previousDisposeMethod = thisDispose == GifDisposeMethod.Undefined ? GifDisposeMethod.None : thisDispose;
        previousGeometry = page;
    }
}
