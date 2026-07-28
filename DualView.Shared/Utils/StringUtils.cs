using System.Diagnostics.CodeAnalysis;

namespace DualView.Shared.Utils;

public static class StringUtils
{
    public static string TrimOrThrowIfEmpty(this string? str)
    {
        if (str == null)
            throw new ArgumentNullException(nameof(str), "string to trim is null");

        if (str.Length == 0)
            throw new ArgumentException("String to trim is empty", nameof(str));

        var result = str.Trim();
        if (result.Length == 0)
            throw new ArgumentException("Trimmed string is empty", nameof(str));

        return result;
    }

    [return: NotNullIfNotNull(nameof(str))]
    public static string? RemoveDuplicatePromptParts(string? str)
    {
        if (string.IsNullOrWhiteSpace(str))
            return str;

        var tags = SplitOnCommaOutsideParentheses(str);

        return string.Join(", ", tags.Distinct());
    }

    public static string[] SplitOnCommaOutsideParentheses(this string str)
    {
        var result = new List<string>();
        int depth = 0;
        int lastStart = 0;
        for (int i = 0; i < str.Length; ++i)
        {
            char c = str[i];
            if (c == '(')
            {
                ++depth;
            }
            else if (c == ')')
            {
                if (depth > 0)
                    --depth;
            }
            else if (c == ',' && depth == 0)
            {
                result.Add(str.Substring(lastStart, i - lastStart));
                lastStart = i + 1;
            }
        }

        result.Add(str.Substring(lastStart));

        return result
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
    }
}
