namespace Backend.Models;

public class IgnoredDuplicate
{
    public IgnoredDuplicate(long mediaFileId1, long mediaFileId2)
    {
        MediaFileId1 = mediaFileId1;
        MediaFileId2 = mediaFileId2;
    }

    public long MediaFileId1 { get; set; }
    public MediaFile MediaFile1 { get; set; } = null!;

    public long MediaFileId2 { get; set; }
    public MediaFile MediaFile2 { get; set; } = null!;
}
