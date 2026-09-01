namespace Backend.Services;

public interface ICurlDownloadService
{
    public Task<string> DownloadAsync(string url,
        IEnumerable<KeyValuePair<string, string>> headers,
        string? referrer,
        IReadOnlyDictionary<string, string> cookies,
        CancellationToken cancellationToken);
}
