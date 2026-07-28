namespace DualView.GUI.ViewModels;

public class InformationWindowViewModel : ViewModelBase
{
    public string Title
    {
        get;
        set => SetProperty(ref field, value);
    } = "Information";

    public string Message
    {
        get;
        set => SetProperty(ref field, value);
    } = "No message provided.";
}
