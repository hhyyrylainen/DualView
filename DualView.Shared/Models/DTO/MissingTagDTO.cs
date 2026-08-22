using DualView.Shared.Models.Enums;

namespace DualView.Shared.Models.DTO;

public class MissingTagDTO
{
    public string Tag { get; set; } = string.Empty;
    public MissingTagTarget Target { get; set; }
    public long TargetId { get; set; }
}
