using DualView.Shared.Utils;

namespace DualView.Shared.Tests.MediaTests;

public class MediaHashTests
{
    [Fact]
    public async Task CalculateMediaHash_IsStableAndResetsStreamPosition()
    {
        await using var stream = new MemoryStream("test image data"u8.ToArray());

        // Intentionally using different method to test consistency.
        // ReSharper disable once MethodHasAsyncOverload
        var first = MediaHash.CalculateMediaHash(stream);
        stream.Position = stream.Length;
        var second = await MediaHash.CalculateMediaHashAsync(stream);

        Assert.Equal(first, second);
        Assert.Equal("_FDxo8nL8BVNfch5mERmJMi3j4TFy+9PgTmgyL4eSXY=", first);
        Assert.Equal(stream.Length, stream.Position);
    }
}
