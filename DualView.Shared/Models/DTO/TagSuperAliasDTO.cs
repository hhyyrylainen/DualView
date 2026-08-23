namespace DualView.Shared.Models.DTO;

public class TagSuperAliasDTO
{
    public TagSuperAliasDTO(string alias, string expanded)
    {
        Alias = alias;
        Expanded = expanded;
    }

    public string Alias { get; set; }
    public string Expanded { get; set; }
}
