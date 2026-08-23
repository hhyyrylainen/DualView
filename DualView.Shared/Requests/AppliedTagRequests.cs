using System.ComponentModel.DataAnnotations;
using DualView.Shared.Models.DTO;

namespace DualView.Shared.Requests;

public class AddAppliedTagRequest
{
    [Required]
    public long TagId { get; set; }
    public List<long>? ModifierIds { get; set; }
    public long? CombinedWithAppliedTagId { get; set; }
    public string? CombineWord { get; set; }

    /// <summary>
    ///   Note: when set this is the information that takes precedence. So client should usually parse first and
    ///   then send the parsed tag.
    /// </summary>
    public AppliedTagDTO? ParsedTag { get; set; }
}

public class AddParsedAppliedTagsToMediaRequest
{
    [Required]
    public List<long> MediaIds { get; set; } = new();

    [Required]
    public List<AppliedTagDTO> AppliedTags { get; set; } = new();
}
