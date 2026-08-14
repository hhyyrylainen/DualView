namespace DualView.Shared.Requests;

public class UpdateUploadSectionRequest
{
    public string Name { get; set; } = string.Empty;
    public bool KeepTarget { get; set; }
    public bool RemoveAfterImport { get; set; }
    public long TargetFolderId { get; set; }
    public string TargetCollectionName { get; set; } = string.Empty;
}

public class UploadSectionMediaRequest
{
    public List<long> MediaIds { get; set; } = new();
}
