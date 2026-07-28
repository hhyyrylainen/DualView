using Avalonia.Controls;

namespace DualView.GUI.Views;

public partial class InformationWindow : Window
{
    public InformationWindow()
    {
        InitializeComponent();

        CloseButton.Click += (sender, args) => Close();
    }
}
