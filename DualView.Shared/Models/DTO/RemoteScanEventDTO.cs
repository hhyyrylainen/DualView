namespace DualView.Shared.Models.DTO;

/// <summary>
///   Describes an event recorded while processing a remote scan or download.
/// </summary>
public sealed class RemoteScanEventDTO
{
    /// <summary>
    ///   Gets the time at which the event occurred.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    ///   Gets the event category.
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    ///   Gets the main URL associated with the event.
    /// </summary>
    public string? MainUrl { get; init; }

    /// <summary>
    ///   Gets the referrer URL associated with the event.
    /// </summary>
    public string? ReferrerUrl { get; init; }

    /// <summary>
    ///   Gets the failure or status detail.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    ///   Gets the retry attempt associated with the event.
    /// </summary>
    public int? Attempt { get; init; }
}
