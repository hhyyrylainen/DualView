using DualView.Shared.Utils;

namespace DualView.Shared.Tests.Utils.Tests;

public class StringUtilsTests
{
    [Fact]
    public void StringUtils_SplitOnCommaOutsideParentheses_Works()
    {
        var input = "tag1, (tag2, tag3:1.1), tag4, ((nested, tag), tag5)";
        var result = StringUtils.SplitOnCommaOutsideParentheses(input);

        Assert.Equal(4, result.Length);
        Assert.Equal("tag1", result[0]);
        Assert.Equal("(tag2, tag3:1.1)", result[1]);
        Assert.Equal("tag4", result[2]);
        Assert.Equal("((nested, tag), tag5)", result[3]);
    }

    [Theory]
    [InlineData("first,,second", "first|second")]
    [InlineData(",first,second,", "first|second")]
    [InlineData("first, ,second", "first|second")]
    [InlineData("(a,b),,c", "(a,b)|c")]
    public void StringUtils_SplitOnCommaOutsideParentheses_HandlesEmptyEntries(string input, string expectedJoined)
    {
        var result = StringUtils.SplitOnCommaOutsideParentheses(input);
        var expected = expectedJoined.Split('|');
        Assert.Equal(expected, result);
    }
}
