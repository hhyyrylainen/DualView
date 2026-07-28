using System;

namespace DualView.GUI.Services;

public interface IGuiConfigurationService
{
    public Uri BackendUrl { get; }

    public string? FfmpegLibraryPath { get; }
}
