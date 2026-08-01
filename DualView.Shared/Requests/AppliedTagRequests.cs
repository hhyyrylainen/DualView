using System.ComponentModel.DataAnnotations;

namespace DualView.Shared.Requests;

public class AddAppliedTagRequest
{
    [Required]
    public long TagId { get; set; }
    public List<long>? ModifierIds { get; set; }
    public long? CombinedWithAppliedTagId { get; set; }
    public string? CombineWord { get; set; }
}
