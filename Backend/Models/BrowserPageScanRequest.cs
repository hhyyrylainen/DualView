namespace Backend.Models;

/// <summary>
///   Requests that a connected browser load a page and return its rendered HTML.
/// </summary>
public sealed class BrowserPageScanRequest
{
    private int claimed;

    /// <summary>
    ///   Gets the identifier used to correlate the browser response.
    /// </summary>
    public string ScanId { get; }

    /// <summary>
    ///   Gets the page URL to load in the browser.
    /// </summary>
    public string Url { get; }

    /// <summary>
    ///   Creates a request for the given page URL.
    /// </summary>
    /// <param name="scanId">Unique identifier for the scan request.</param>
    /// <param name="url">Page URL to load.</param>
    public BrowserPageScanRequest(string scanId, string url)
    {
        ScanId = scanId;
        Url = url;
    }

    /// <summary>
    ///   Claims this request for one connected browser so multiple browsers do not load the same page.
    /// </summary>
    public bool TryClaim()
    {
        return Interlocked.CompareExchange(ref claimed, 1, 0) == 0;
    }
}
