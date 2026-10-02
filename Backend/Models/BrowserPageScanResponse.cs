namespace Backend.Models;

/// <summary>
///   Contains the rendered HTML returned by a browser page scan, or the browser-side error.
/// </summary>
public sealed class BrowserPageScanResponse
{
    /// <summary>
    ///   Gets the identifier of the browser scan request.
    /// </summary>
    public string ScanId { get; }

    /// <summary>
    ///   Gets the rendered HTML, or null if the browser could not capture the page.
    /// </summary>
    public string? Html { get; }

    /// <summary>
    ///   Gets the browser-side error, if the page could not be captured.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    ///   Creates a response for a browser page scan.
    /// </summary>
    /// <param name="scanId">Identifier of the original scan request.</param>
    /// <param name="html">Rendered HTML returned by the browser.</param>
    /// <param name="error">Error reported by the browser, if any.</param>
    public BrowserPageScanResponse(string scanId, string? html, string? error)
    {
        ScanId = scanId;
        Html = html;
        Error = error;
    }
}
