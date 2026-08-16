namespace DualView.Shared.Models.DTO;

public class RecentImportSectionDTO
{
    public string Name { get; set; } = string.Empty;
    public DateTime LastUsed { get; set; }

    /// <summary>
    ///   True for sections that currently exist
    /// </summary>
    public bool IsActive { get; set; }
}
