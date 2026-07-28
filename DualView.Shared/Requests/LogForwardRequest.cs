using System.ComponentModel.DataAnnotations;

namespace DualView.Shared.Requests;

/// <summary>
///   Problem reports from clients.
/// </summary>
public class LogForwardRequest
{
    [Required]
    [MaxLength(50)]
    public string Level { get; set; } = string.Empty;

    [Required]
    [MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(10000)]
    public string? Exception { get; set; }
}
