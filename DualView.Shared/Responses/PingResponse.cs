using System.ComponentModel.DataAnnotations;

namespace DualView.Shared.Responses;

public class PingResponse
{
    /// <summary>
    ///   Should be "pong" when received
    /// </summary>
    [Required]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    ///   Should be "DualView" when received
    /// </summary>
    [Required]
    public string AppName { get; set; } = string.Empty;
}
