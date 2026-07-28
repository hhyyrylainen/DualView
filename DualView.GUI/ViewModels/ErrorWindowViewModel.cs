namespace DualView.GUI.ViewModels;

public class ErrorWindowViewModel : ViewModelBase
{
    public string Title
    {
        get;
        set => SetProperty(ref field, value);
    } = "An error has occurred";

    public string ErrorMessage
    {
        get;
        set => SetProperty(ref field, value);
    } = "No extra information available";

    public string Details
    {
        get;
        set => SetProperty(ref field, value);
        // } = string.Empty;
    } = "Test extra details\nFor this";

    public int ErrorCount
    {
        get;
        set => SetProperty(ref field, value);
    } = 1;
}
