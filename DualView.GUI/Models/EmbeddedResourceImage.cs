using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Platform;
using ImageMagick;

namespace DualView.GUI.Models;

/// <summary>
///   An image that is embedded in the application as a resource. And loaded just once when required.
/// </summary>
public class EmbeddedResourceImage : IVisualMediaSource
{
    public const string FolderIcon = "Wenrexa Basket.png";

    private static readonly Dictionary<string, EmbeddedResourceImage> Loaded = new();

    private readonly MagickImage image;
    private readonly string resourceName;

    public EmbeddedResourceImage(string resourceName)
    {
        this.resourceName = resourceName;
        image = new MagickImage();

        // using var stream = typeof(EmbeddedResourceImage).Assembly.GetManifestResourceStream(resourceName);

        var projectName = typeof(EmbeddedResourceImage).Assembly.GetName().Name;

        // Avalonia read variant
        var uri = new Uri($"avares://{projectName}/Assets/{resourceName}");

        using var stream = AssetLoader.Open(uri);

        if (stream == null)
            throw new InvalidOperationException($"Could not find resource {resourceName}");

        image.Read(stream);
    }

    public bool IsAnimated => false;
    public bool HasAudio => false;
    public bool PlayAudio { get; set; }

    public IVisualMediaSource.LoadType LoadStatus { get; private set; } = IVisualMediaSource.LoadType.Thumbnail;

    public static EmbeddedResourceImage GetResource(string resourceName)
    {
        lock (Loaded)
        {
            if (Loaded.TryGetValue(resourceName, out var result))
                return result;

            return Loaded[resourceName] = new EmbeddedResourceImage(resourceName);
        }
    }

    public Task RequestLoadFull()
    {
        LoadStatus = IVisualMediaSource.LoadType.FullSize;
        return Task.CompletedTask;
    }

    public Task RequestLoadThumbnail()
    {
        LoadStatus = IVisualMediaSource.LoadType.Thumbnail;
        return Task.CompletedTask;
    }

    public MagickImage GetCurrentFrame()
    {
        return image;
    }

    public Task<MagickImage> GetCurrentFrameAsync(CancellationToken token)
    {
        return Task.FromResult(image);
    }

    public TimeSpan GetNextFrameTime()
    {
        throw new InvalidOperationException("Embedded resources are not animated");
    }

    public void AdvanceFrame()
    {
        throw new InvalidOperationException("Embedded resources are not animated");
    }

    public Task AdvanceFrameAsync(CancellationToken token)
    {
        throw new InvalidOperationException("Embedded resources are not animated");
    }

    public Task<string> GetName()
    {
        return Task.FromResult(resourceName);
    }

    public IVisualMediaSource Clone()
    {
        // These are static, so we just return ourselves on clone
        return this;
    }

    public void Dispose()
    {
        // As these are shared, these are not disposed
    }
}
