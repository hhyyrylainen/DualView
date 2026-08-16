using Backend.Models;

namespace Backend.Services;

/// <summary>
///   Service shell for parsing tags from strings, to be fully implemented in the next phase of porting.
/// </summary>
public interface ITagParser
{
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

    /// <summary>
    ///   Gets autocomplete suggestions for a partially typed tag string.
    /// </summary>
    /// <param name="tagString">The partial tag string</param>
    /// <param name="maxCount">Maximum number of suggestions to return</param>
    /// <returns>A list of suggested tag strings</returns>
    public Task<List<string>> GetSuggestions(string tagString, int maxCount = 100);
}
