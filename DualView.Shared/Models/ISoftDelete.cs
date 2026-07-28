namespace DualView.Shared.Models;

/// <summary>
///   Things that can be marked as deleted but then deleted permanently later. Requires the last update time to know
///   when data can be cleaned up safely.
/// </summary>
public interface ISoftDelete : IUpdateableModel
{
    public bool IsDeleted { get; set; }
}

public static class SoftDeleteExtensions
{
    public static void MarkAsDeleted(this ISoftDelete model)
    {
        model.IsDeleted = true;
        model.BumpUpdatedAtTime();
    }
}
