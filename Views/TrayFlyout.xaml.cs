using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OpenQCY_Desktop.ViewModels;

namespace OpenQCY_Desktop.Views;

public sealed partial class TrayFlyout : UserControl
{
    public MainPageViewModel ViewModel => App.ViewModel;

    public TrayFlyout()
    {
        InitializeComponent();
    }

    private void OpenDesktop_Click(object sender, RoutedEventArgs e)
    {
        App.ShowMainWindow();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        App.HideTrayFlyout();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        App.ExitApplication();
    }
}
