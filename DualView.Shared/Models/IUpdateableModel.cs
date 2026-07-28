namespace DualView.Shared.Models;

public interface IUpdateableModel
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdateableModel : IUpdateableModel
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public static class UpdateableModelExtensions
{
    public static void BumpUpdatedAtTime(this IUpdateableModel model)
    {
        model.UpdatedAt = DateTime.UtcNow;
    }
}
