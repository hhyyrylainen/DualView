namespace DualView.Shared.Utils;

public static class CollectionUtils
{
    public static Dictionary<TKey, TValue> CloneShallow<TKey, TValue>(this Dictionary<TKey, TValue> dictionary)
        where TKey : notnull
    {
        return new Dictionary<TKey, TValue>(dictionary);
    }
}
