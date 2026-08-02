using System.Security.Cryptography;

namespace DualView.Shared.Utils;

/// <summary>
///   Helpers for media hash calculation. This is done like this to be DualView++ compatible.
/// </summary>
public static class MediaHash
{
    public static string CalculateMediaHash(Stream stream)
    {
        stream.Position = 0;
        var hashBytes = SHA256.HashData(stream);

        // This makes the hashes path safe
        return Convert.ToBase64String(hashBytes).Replace('/', '_');
    }

    public static async Task<string> CalculateMediaHashAsync(Stream stream,
        CancellationToken cancellationToken = default)
    {
        stream.Position = 0;
        var hashBytes = await SHA256.HashDataAsync(stream, cancellationToken);

        // This makes the hashes path safe
        return Convert.ToBase64String(hashBytes).Replace('/', '_');
    }
}
