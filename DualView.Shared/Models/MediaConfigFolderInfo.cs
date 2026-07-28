namespace DualView.Shared.Models;

public class MediaConfigFolderInfo(string name, string primaryFolder)
{
    public string Name { get; set; } = name;

    public string PrimaryFolder { get; set; } = primaryFolder;

    public List<string>? SecondaryFolders { get; set; }
}
