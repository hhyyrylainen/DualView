using Backend.Models;

namespace Backend.Services;

/// <summary>
///   Service shell for parsing tags from strings, to be fully implemented in the next phase of porting.
/// </summary>
public interface ITagParser
{
    // NOTE: the old C++ parsing code has a bug in multi word tags so when porting it to C# try to avoid copying the bug

    /// <summary>
    ///   Parses a tag string into an AppliedTag.
    /// </summary>
    /// <param name="tagString">String to parse</param>
    /// <returns>The parsed applied tag, or null if invalid</returns>
    public Task<AppliedTag?> ParseTag(string tagString);

    /// <summary>
    ///   Finds a tag by name or alias.
    /// </summary>
    public Task<Tag?> FindTag(string name);
}

public class TagParser : ITagParser
{
    private readonly IDatabaseService databaseService;

    public TagParser(IDatabaseService databaseService)
    {
        this.databaseService = databaseService;
    }

    public async Task<AppliedTag?> ParseTag(string tagString)
    {
        // TODO: Port recursive parsing logic from C++ (DualView::ParseTagFromString)
        // 1. Exact match (Tags/Aliases)
        // 2. Modifiers
        // 3. Composites
        // 4. Heuristics
        return null;
    }

    public async Task<Tag?> FindTag(string name)
    {
        // TODO: Implementation
        return null;
    }
}
