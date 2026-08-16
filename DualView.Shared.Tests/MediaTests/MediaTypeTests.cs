using DualView.Shared.Models.Enums;

namespace DualView.Shared.Tests.MediaTests;

public class MediaTypeTests
{
    [Theory]
    [InlineData(".jpg", MediaType.Jpeg)]
    [InlineData(".jpeg", MediaType.Jpeg)]
    [InlineData(".gif", MediaType.Gif)]
    [InlineData(".webp", MediaType.Webp)]
    [InlineData(".mp4", MediaType.Mp4)]
    public void TypeFromExtension_MapsSupportedExtensions(string extension, MediaType expected)
    {
        Assert.Equal(expected, MediaTypeExtensions.TypeFromExtension(extension));
    }

    [Fact]
    public void AnimatedAndImageFlagsMatchMediaSemantics()
    {
        Assert.True(MediaType.Gif.IsImage());
        Assert.True(MediaType.Gif.IsAnimated());
        Assert.True(MediaType.Mp4.IsAnimated());
        Assert.False(MediaType.Mp4.IsImage());
        Assert.Equal(MediaType.WebpAnimated, MediaType.Webp.MakeAnimated());
    }
}
