using System.ComponentModel.DataAnnotations;

namespace DualView.Shared.Models;

public class DualViewSettings
{
    public int Id { get; set; } = 1;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? LocalMediaStorageLocation { get; set; }

    [MaxLength(500)]
    public string? GUIStartupCommand { get; set; }

    [MaxLength(500)]
    public string? AIRunManagerUrl { get; set; }

    [MaxLength(500)]
    public string? AIRunManagerAccessKey { get; set; }

    public int AudioBufferingMs { get; set; } = 60;

    public bool HoldPurge { get; set; }

    public void UpdateFromClient(DualViewSettings newSettings)
    {
        if (newSettings.LocalMediaStorageLocation != null)
            LocalMediaStorageLocation = newSettings.LocalMediaStorageLocation;

        if (newSettings.GUIStartupCommand != null)
            GUIStartupCommand = newSettings.GUIStartupCommand;

        AudioBufferingMs = newSettings.AudioBufferingMs;
        HoldPurge = newSettings.HoldPurge;
    }
}
