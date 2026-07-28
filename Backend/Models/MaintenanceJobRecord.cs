using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

/// <summary>
///   When some maintenance was done
/// </summary>
public class MaintenanceJobRecord
{
    public MaintenanceJobRecord(string name)
    {
        Name = name;
    }

    [Key]
    [MaxLength(128)]
    public string Name { get; set; }

    public DateTime LastPerformed { get; set; }

    public DateTime NextRunAfter { get; set; }

    public bool Failed { get; set; }

    [MaxLength(10000)]
    public string StatusMessage { get; set; } = string.Empty;
}
