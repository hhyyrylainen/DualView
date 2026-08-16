using System.Text;
using DualView.Shared.Models.DTO;

namespace DualView.Shared.Utils;

public static class AppliedTagText
{
    public static string ToText(AppliedTagDTO appliedTag)
    {
        var builder = new StringBuilder();

        AppendTo(builder, appliedTag);

        return builder.ToString();
    }

    private static void AppendTo(StringBuilder builder, AppliedTagDTO appliedTag)
    {
        foreach (var modifier in appliedTag.Modifiers)
        {
            AppendPart(builder, modifier.Name);
        }

        if (appliedTag.Tag != null)
        {
            AppendPart(builder, appliedTag.Tag.Name);
        }
        else
        {
            // This is really an error condition as the tag shouldn't be missing
            AppendPart(builder, "UNSET TAG");
        }

        if (appliedTag.CombinedWith != null)
        {
            AppendPart(builder, appliedTag.CombineWord ?? "UNSET WORD");
            AppendTo(builder, appliedTag.CombinedWith);
        }
    }

    private static void AppendPart(StringBuilder builder, string text)
    {
        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        builder.Append(text);
    }
}
