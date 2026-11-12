using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using OpenQCY_Desktop.ViewModels;

namespace OpenQCY_Desktop;

public sealed partial class MainPage : Page
{
    public MainPageViewModel ViewModel => App.ViewModel;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainPage_Loaded;
        await ViewModel.StartAsync();
    }

    private GestureViewModel? _sheetGesture;

    private void GestureButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: GestureViewModel gesture })
        {
            return;
        }

        _sheetGesture = gesture;
        GestureSheetTitle.Text = $"{gesture.Title} · {(gesture.IsLeft ? "left" : "right")} earbud";
        GestureSheetOptions.Children.Clear();
        var accent = (Brush)Application.Current.Resources["OpenQCYAccentBrush"];
        var stroke = (Brush)Application.Current.Resources["OpenQCYStrokeBrush"];
        foreach (var action in GestureViewModel.Actions)
        {
            var isSelected = action == gesture.Action;
            if (GestureSheetOptions.Children.Count > 0)
            {
                GestureSheetOptions.Children.Add(new Border { Height = 1, Margin = new Thickness(14, 0, 14, 0), Background = stroke });
            }

            var row = new Grid();
            var label = new TextBlock { Text = action, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(label);
            if (isSelected)
            {
                label.Foreground = accent;
                label.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
                row.Children.Add(new FontIcon
                {
                    Glyph = "\uE73E",
                    FontSize = 16,
                    Foreground = accent,
                    HorizontalAlignment = HorizontalAlignment.Right,
                });
            }

            var option = new Button
            {
                Content = row,
                Padding = new Thickness(14, 16, 14, 16),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(14),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(option, action);
            option.Click += (_, _) =>
            {
                gesture.Action = action;
                CloseGestureSheet();
            };
            GestureSheetOptions.Children.Add(option);
        }

        OpenGestureSheet();
    }

    private void OpenGestureSheet()
    {
        GestureSheetPanel.MaxHeight = Math.Max(240, ActualHeight * 0.85);
        GestureSheet.Visibility = Visibility.Visible;
        GestureSheetPanel.UpdateLayout();
        AnimateGestureSheet(GestureSheetPanel.ActualHeight, 0, 0, 1, 220, EasingMode.EaseOut, null);
    }

    private void CloseGestureSheet()
    {
        if (GestureSheet.Visibility != Visibility.Visible)
        {
            return;
        }

        _sheetGesture = null;
        AnimateGestureSheet(0, GestureSheetPanel.ActualHeight, 1, 0, 160, EasingMode.EaseIn, () =>
        {
            GestureSheet.Visibility = Visibility.Collapsed;
            GestureSheetOptions.Children.Clear();
        });
    }

    private void AnimateGestureSheet(
        double fromOffset,
        double toOffset,
        double fromOpacity,
        double toOpacity,
        int milliseconds,
        EasingMode easing,
        Action? completed)
    {
        var duration = new Duration(TimeSpan.FromMilliseconds(milliseconds));
        var slide = new DoubleAnimation
        {
            From = fromOffset,
            To = toOffset,
            Duration = duration,
            EasingFunction = new CubicEase { EasingMode = easing },
        };
        Storyboard.SetTarget(slide, GestureSheetOffset);
        Storyboard.SetTargetProperty(slide, "Y");
        var fade = new DoubleAnimation { From = fromOpacity, To = toOpacity, Duration = duration };
        Storyboard.SetTarget(fade, GestureSheetBackdrop);
        Storyboard.SetTargetProperty(fade, "Opacity");
        var storyboard = new Storyboard();
        storyboard.Children.Add(slide);
        storyboard.Children.Add(fade);
        if (completed is not null)
        {
            storyboard.Completed += (_, _) => completed();
        }

        storyboard.Begin();
    }

    private void GestureSheetBack_Click(object sender, RoutedEventArgs e) => CloseGestureSheet();

    private void GestureSheetBackdrop_Tapped(object sender, TappedRoutedEventArgs e) => CloseGestureSheet();

    private void SectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string section })
        {
            return;
        }

        CloseGestureSheet();

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
