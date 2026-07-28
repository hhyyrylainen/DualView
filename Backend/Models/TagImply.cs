namespace Backend.Models;

public class TagImply
{
    public TagImply(long primaryTagId, long toApplyTagId)
    {
        PrimaryTagId = primaryTagId;
        ToApplyTagId = toApplyTagId;
    }

    public long PrimaryTagId { get; set; }
    public Tag PrimaryTag { get; set; } = null!;

    public long ToApplyTagId { get; set; }
    public Tag ToApplyTag { get; set; } = null!;
}
