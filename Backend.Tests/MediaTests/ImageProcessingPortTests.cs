using Backend.Models;
using Backend.Services;
using Backend.Utilities;
using DualView.Shared.Models.Enums;
using DualView.Shared.Utils;
using ImageMagick;

namespace Backend.Tests.MediaTests;

/// <summary>
///   Direct equivalents for the legacy ImageMagick, hash and thumbnail sizing tests.
/// </summary>
public class ImageProcessingPortTests
{
    private static string TestData(string fileName)
    {
        return Path.Combine(AppContext.BaseDirectory, "TestData", fileName);
    }

    [Fact]
    public async Task LegacyJpegFixture_HasExpectedDimensionsAndHash()
    {
        var path = TestData("7c2c2141cf27cb90620f80400c6bc3c4.jpg");
        using var image = new MagickImage(path);
        await using var stream = File.OpenRead(path);

        Assert.Equal(914u, image.Width);
        Assert.Equal(1280u, image.Height);
        Assert.Equal(MediaType.Jpeg, MediaTypeExtensions.TypeFromExtension(Path.GetExtension(path)));
        Assert.False(string.IsNullOrWhiteSpace(await MediaHash.CalculateMediaHashAsync(stream)));
    }

    [Fact]
    public async Task LegacyGifFixture_HasExpectedDimensionsAndFrames()
    {
        var path = TestData("bird bathing.gif");
        using var images = new MagickImageCollection(path);

        Assert.Equal(250u, images[0].Width);
        Assert.Equal(250u, images[0].Height);
        Assert.Equal(142, images.Count);
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData(914, 1280, 182, 256)]
    [InlineData(1280, 914, 256, 182)]
    [InlineData(1, 1, 256, 256)]
    public void ThumbnailDimensions_AreEvenAndFitWithinTarget(int width, int height, int expectedWidth,
        int expectedHeight)
    {
        var result = MediaProcessingService.GetDivisibleByTwoDimensions(width, height);

        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
        Assert.Equal(0, result.Width % 2);
        Assert.Equal(0, result.Height % 2);
    }

    [Fact]
    public void MediaFileStoragePaths_AreStableAndSafe()
    {
        var media = new MediaFile("photo.jpg", "abcdef1234");

        Assert.Equal("originalMedia/ab/cd/ef1234.jpg", media.PathRelativeToStorage());
        Assert.Equal("originalMedia/ab/cd/ef1234_cropped.jpg", media.CroppedPathRelativeToStorage());
    }

    [Fact]
    public void FileProbe_MapsImageFormats()
    {
        Assert.Equal(".jpg", FileProbe.GetExtensionForFormat(MagickFormat.Jpeg));
        Assert.Equal(".gif", FileProbe.GetExtensionForFormat(MagickFormat.Gif));
        Assert.Equal(".webp", FileProbe.GetExtensionForFormat(MagickFormat.WebP));
    }
}
