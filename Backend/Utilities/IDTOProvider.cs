namespace Backend.Utilities;

public interface IDTOProvider<T>
{
    public T GetDTO();
}

public interface IInfoProvider<T>
{
    public T GetInfo();
}
