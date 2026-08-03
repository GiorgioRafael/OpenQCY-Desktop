using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenQCY_Desktop.ViewModels;

namespace OpenQCY_Desktop;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel => App.ViewModel;

    public MainPage()
    {
        InitializeComponent();
    }

    private void SectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string section })
        {
            return;
        }

        OverviewSection.Visibility = section == "Overview" ? Visibility.Visible : Visibility.Collapsed;
        SoundSection.Visibility = section == "Sound" ? Visibility.Visible : Visibility.Collapsed;
        ControlsSection.Visibility = section == "Controls" ? Visibility.Visible : Visibility.Collapsed;
        ConnectionSection.Visibility = section == "Connection" ? Visibility.Visible : Visibility.Collapsed;
        GeneralSection.Visibility = section == "General" ? Visibility.Visible : Visibility.Collapsed;

        var selectedBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(22, 255, 255, 255));
        var clearBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));

        OverviewNavButton.Background = section == "Overview" ? selectedBrush : clearBrush;
        SoundNavButton.Background = section == "Sound" ? selectedBrush : clearBrush;
        ControlsNavButton.Background = section == "Controls" ? selectedBrush : clearBrush;
        ConnectionNavButton.Background = section == "Connection" ? selectedBrush : clearBrush;
        GeneralNavButton.Background = section == "General" ? selectedBrush : clearBrush;
    }
}
