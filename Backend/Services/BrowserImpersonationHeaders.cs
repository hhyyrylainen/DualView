namespace Backend.Services;

/// <summary>
///   Stores browser request headers captured from a browser plugin websocket connection.
/// </summary>
public sealed class BrowserImpersonationHeaders
{
    private static readonly HashSet<string> HeadersNotSuitableForReplay = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Content-Length",
        "Host",
        "Keep-Alive",
        "Proxy-Connection",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade",
        "Sec-WebSocket-Accept",
        "Sec-WebSocket-Extensions",
        "Sec-WebSocket-Key",
        "Sec-WebSocket-Protocol",
        "Sec-WebSocket-Version",
    };

    /// <summary>
    ///   Initializes a new instance of the <see cref="BrowserImpersonationHeaders"/> class.
    /// </summary>
    /// <param name="headers">The headers to store.</param>
    public BrowserImpersonationHeaders(IReadOnlyDictionary<string, string> headers)
    {
        Headers = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    ///   Gets the captured headers. Cookie headers are never included.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>
    ///   Adds captured browser headers to an HTTP request without replacing explicitly configured headers.
    ///   The captured user agent always replaces the request user agent.
    /// </summary>
    /// <param name="request">The request to configure.</param>
    public void ConfigureHttpRequest(HttpRequestMessage request)
    {
        foreach (var header in Headers)
        {
            if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
                HeadersNotSuitableForReplay.Contains(header.Key))
                continue;

            if (header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
            {
                request.Headers.Remove("User-Agent");
                request.Headers.TryAddWithoutValidation("User-Agent", header.Value);
                continue;
            }

            if (request.Headers.Contains(header.Key) || request.Content?.Headers.Contains(header.Key) == true)
                continue;

            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }
}
