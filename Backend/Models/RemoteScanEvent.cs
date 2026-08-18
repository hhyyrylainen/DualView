namespace Backend.Models;

/// <summary>
///   An event recorded while processing a remote scan or download.
/// </summary>
public sealed class RemoteScanEvent
{
    /// <summary>
    ///   Gets the time at which the event occurred.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    ///   Gets the event category.
    /// </summary>
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    ///   Gets the URL associated with the event.
    /// </summary>
    public string? ImageUrl { get; init; }

    /// <summary>
    ///   Gets the failure or status detail.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    ///   Gets the retry attempt associated with the event.
    /// </summary>
    public int? Attempt { get; init; }
}
