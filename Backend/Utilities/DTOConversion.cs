namespace Backend.Utilities;

public static class DTOConversion
{
    public static IEnumerable<TDTO> ConvertToDTO<TOriginal, TDTO>(this IEnumerable<TOriginal> sequence)
        where TOriginal : IDTOProvider<TDTO>
    {
        foreach (var provider in sequence)
        {
            yield return provider.GetDTO();
        }
    }

    public static List<TDTO> ConvertToDTO<TOriginal, TDTO>(this List<TOriginal> sequence)
        where TOriginal : IDTOProvider<TDTO>
    {
        var result = new List<TDTO>(sequence.Count);
        foreach (var provider in sequence)
        {
            result.Add(provider.GetDTO());
        }

        return result;
    }

    public static IEnumerable<TInfo> ConvertToInfo<TOriginal, TInfo>(this IEnumerable<TOriginal> sequence)
        where TOriginal : IInfoProvider<TInfo>
    {
        foreach (var provider in sequence)
        {
            yield return provider.GetInfo();
        }
    }

    public static List<TInfo> ConvertToInfo<TOriginal, TInfo>(this List<TOriginal> sequence)
        where TOriginal : IInfoProvider<TInfo>
    {
        var result = new List<TInfo>(sequence.Count);
        foreach (var provider in sequence)
        {
            result.Add(provider.GetInfo());
        }

        return result;
    }
}
