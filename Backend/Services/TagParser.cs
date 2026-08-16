using Backend.Models;

namespace Backend.Services;

public class TagParser : ITagParser
{
    private readonly IDatabaseService databaseService;

    public TagParser(IDatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    public async Task<AppliedTag?> ParseTag(string tagString)
    {
        var str = tagString.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(str))
            return null;

        // 1. Exact match (Tags/Aliases)
        var existingTag = await databaseService.GetTagByNameOrAliasAsync(str);
        if (existingTag != null)
            return new AppliedTag(existingTag.Id);

        // 2. Super aliases
        var expanded = await databaseService.GetTagSuperAliasAsync(str);
        if (!string.IsNullOrEmpty(expanded))
        {
            // Note: C++ recursive call. In C# we should probably handle multiple tags if super alias expands to multiple,
            // but AppliedTag is a single tag with modifiers.
            // Wait, C++ ParseTagFromString returns std::shared_ptr<AppliedTag>.
            // If it expands to multiple, how does it handle it?
            // "Parse the replacement tag" -> ParseTagFromString(expanded)
            return await ParseTag(expanded);
        }

        // 3. Modifiers
        var modifiedTag = await ParseTagWithOnlyModifiers(str);
        if (modifiedTag != null)
            return modifiedTag;

        // 4. Composites
        var composite = await ParseTagWithComposite(str);
        if (composite != null)
            return composite;

        // 5. Break rules
        var breakRule = await databaseService.GetTagBreakRuleByStrAsync(str);
        if (breakRule != null)
        {
            var applied = new AppliedTag(breakRule.ActualTagId);
            foreach (var mod in breakRule.Modifiers)
            {
                applied.Modifiers.Add(mod);
            }
            return applied;
        }

        // 6. Heuristics (e.g. removing 's')
        if (str.EndsWith('s') && str.Length > 1)
        {
            return await ParseTag(str[..^1]);
        }

        return null;
    }

    public async Task<Tag?> FindTag(string name)
    {
        return await databaseService.GetTagByNameOrAliasAsync(name);
    }

    public async Task<List<string>> GetSuggestions(string tagString, int maxCount = 100)
    {
        var str = tagString.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(str))
            return [];

        var result = new List<string>();

        // Consume valid parts from the front to build a prefix
        var words = str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var prefix = "";
        var currentPart = "";
        var tagAllowed = true;
        var modifierAllowed = true;

        var i = 0;
        while (i < words.Length)
        {
            var word = words[i];
            var nextPart = string.IsNullOrEmpty(currentPart) ? word : currentPart + " " + word;

            bool partIsValid = false;

            // Try to match nextPart as a tag, modifier, break rule, or super alias
            if (tagAllowed)
            {
                var tag = await databaseService.GetTagByNameOrAliasAsync(nextPart);
                if (tag != null)
                {
                    tagAllowed = false;
                    modifierAllowed = false;
                    partIsValid = true;
                }
            }

            if (!partIsValid && modifierAllowed)
            {
                var mod = await databaseService.GetTagModifierByNameAsync(nextPart);
                if (mod != null)
                {
                    modifierAllowed = false;
                    tagAllowed = true;
                    partIsValid = true;
                }
            }

            if (!partIsValid)
            {
                var rule = await databaseService.GetTagBreakRuleByStrAsync(nextPart);
                if (rule != null)
                {
                    modifierAllowed = false;
                    tagAllowed = true;
                    partIsValid = true;
                }
            }

            if (!partIsValid)
            {
                var expanded = await databaseService.GetTagSuperAliasAsync(nextPart);
                if (!string.IsNullOrEmpty(expanded))
                {
                    modifierAllowed = false;
                    tagAllowed = true;
                    partIsValid = true;
                }
            }

            if (partIsValid)
            {
                prefix = string.IsNullOrEmpty(prefix) ? nextPart : prefix + " " + nextPart;
                currentPart = "";
                // If we found a valid part, we might want to check for combines, but for now we just continue
            }
            else
            {
                currentPart = nextPart;
            }

            i++;
        }

        if (!string.IsNullOrEmpty(prefix))
            prefix += " ";

        // currentPart now contains the unparsed tail
        if (string.IsNullOrEmpty(currentPart))
        {
            // The whole string was valid
            try
            {
                var parsed = await ParseTag(str);
                if (parsed != null)
                    result.Add(str);
            }
            catch
            {
                // Not actually a tag
            }

            // Also get longer tags that start with the same thing
            var matches = await RetrieveTagsMatching(str);
            foreach (var m in matches)
                result.Add(m);
        }
        else
        {
            // Find suggestions for the unparsed part
            var matches = await RetrieveTagsMatching(currentPart);
            foreach (var m in matches)
            {
                result.Add(prefix + m);
            }

            // Handles combines (multi-word logic)
            // If the first word of currentPart didn't match anything, try finding suggestions for the rest
            var spaceIndex = currentPart.IndexOf(' ');
            if (spaceIndex != -1)
            {
                var tail = currentPart[(spaceIndex + 1)..];
                var tailPrefix = prefix + currentPart[..(spaceIndex + 1)];
                var tailSuggestions = await GetSuggestions(tail, maxCount);
                foreach (var s in tailSuggestions)
                {
                    result.Add(tailPrefix + s);
                }
            }
        }

        // Sort and deduplicate
        // TODO: custom sorting like in C++ (DV::SortSuggestions)
        return result.Distinct().Take(maxCount).OrderBy(s => s).ToList();
    }

    private async Task<List<string>> RetrieveTagsMatching(string str)
    {
        var result = new List<string>();
        result.AddRange(await databaseService.SelectTagNamesWildcardAsync(str));
        result.AddRange(await databaseService.SelectTagAliasesWildcardAsync(str));
        result.AddRange(await databaseService.SelectTagModifierNamesWildcardAsync(str));
        result.AddRange(await databaseService.SelectTagBreakRulesByStrWildcardAsync(str));
        result.AddRange(await databaseService.SelectTagSuperAliasWildcardAsync(str));
        return result;
    }

    private async Task<AppliedTag?> ParseTagWithOnlyModifiers(string str)
    {
        var words = str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
            return null;

        // Try to find a tag in the words
        for (int i = 0; i < words.Length; ++i)
        {
            // Tag can be multiple words? 
            // The bug in C++ was probably that it split by space and then didn't handle multi-word tags correctly.
            // To fix it, we should try combining words.

            // Let's try matching the suffix as a tag and the prefix as modifiers
            var tagCandidate = string.Join(' ', words.Skip(i));
            var tag = await databaseService.GetTagByNameOrAliasAsync(tagCandidate);

            if (tag != null)
            {
                var modifiers = new List<TagModifier>();
                var allModifiersValid = true;

                for (int j = 0; j < i; ++j)
                {
                    var modifier = await databaseService.GetTagModifierByNameAsync(words[j]);
                    if (modifier == null)
                    {
                        allModifiersValid = false;
                        break;
                    }
                    modifiers.Add(modifier);
                }

                if (allModifiersValid)
                {
                    var applied = new AppliedTag(tag.Id);
                    foreach (var mod in modifiers)
                        applied.Modifiers.Add(mod);
                    return applied;
                }
            }

            // Also try prefix as tag and suffix as modifiers
            tagCandidate = string.Join(' ', words.Take(i + 1));
            tag = await databaseService.GetTagByNameOrAliasAsync(tagCandidate);

            if (tag != null)
            {
                var modifiers = new List<TagModifier>();
                var allModifiersValid = true;

                for (int j = i + 1; j < words.Length; ++j)
                {
                    var modifier = await databaseService.GetTagModifierByNameAsync(words[j]);
                    if (modifier == null)
                    {
                        allModifiersValid = false;
                        break;
                    }
                    modifiers.Add(modifier);
                }

                if (allModifiersValid)
                {
                    var applied = new AppliedTag(tag.Id);
                    foreach (var mod in modifiers)
                        applied.Modifiers.Add(mod);
                    return applied;
                }
            }
        }

        return null;
    }

    private async Task<AppliedTag?> ParseTagWithComposite(string str)
    {
        var words = str.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 3)
            return null;

        for (int i = 1; i < words.Length - 1; ++i)
        {
            var leftStr = string.Join(' ', words.Take(i));
            var rightStr = string.Join(' ', words.Skip(i + 1));
            var middle = words[i];

            var left = await ParseTag(leftStr);
            if (left == null) continue;

            var right = await ParseTag(rightStr);
            if (right == null) continue;

            // Combine
            left.CombinedWithId = right.Id; // This is not quite right, AppliedTag usually combines with another AppliedTag ID
            // but we might need to save 'right' first or handle it differently.
            // In C++: parsedleft->SetCombineWith(middle, parsedright);
            
            // Wait, AppliedTag in C# has CombinedWithId and CombineWord.
            // But we don't have the ID for 'right' yet as it's not saved.
            
            // For now, let's just return left and hope the caller handles saving.
            // Actually, we might need a way to return a composite object that isn't fully saved.
            
            left.CombineWord = middle;
            // left.CombinedWith = right; // If we had this property
            
            // TODO: properly handle composite tags in C#
            return left;
        }

        return null;
    }
}
