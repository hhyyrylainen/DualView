using DualView.Shared.Models.DTO;
using DualView.Shared.Models.Enums;

namespace Backend.Services;

public interface IMissingTagService
{
    public Task ReportTagAsync(string tag, MissingTagTarget target, long targetId);
    public Task<List<MissingTagDTO>> GetMissingTagsAsync();
    public Task IgnoreTagAsync(string tag);
    public Task ResetIgnoredTagsAsync();
}
