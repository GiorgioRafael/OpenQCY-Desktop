using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace OpenQCY_Desktop;

public sealed partial class MainWindow : Window
{
    private TrayFlyoutWindow? _trayFlyoutWindow;

    public IRelayCommand ShowTrayFlyoutCommand { get; }

    public MainWindow()
    {
        ShowTrayFlyoutCommand = new RelayCommand(ShowTrayFlyout);
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon-v2.ico");
        AppWindow.Resize(new SizeInt32(1180, 780));
        CenterOnCurrentDisplay();
        AppWindow.Closing += AppWindow_Closing;

        RootFrame.Navigate(typeof(MainPage));
        TrayIcon.ForceCreate(enablesEfficiencyMode: false);

#if DEBUG
        if (Environment.GetCommandLineArgs().Contains("--show-tray-preview", StringComparer.OrdinalIgnoreCase))
        {
            var previewTimer = DispatcherQueue.CreateTimer();
            previewTimer.Interval = TimeSpan.FromSeconds(2);
            previewTimer.IsRepeating = false;
            previewTimer.Tick += (_, _) => ShowTrayFlyout();
            previewTimer.Start();
        }
#endif
    }

    public void ShowFromTray()
    {
        WindowExtensions.Show(this, disableEfficiencyMode: true);
        Activate();
    }

    public void DisposeTrayIcon()
    {
        _trayFlyoutWindow?.CloseForExit();
        TrayIcon.Dispose();
    }

    public void HideTrayFlyout()
    {
        _trayFlyoutWindow?.Hide();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (App.IsExiting)
        {
            return;
        }

        args.Cancel = true;
        WindowExtensions.Hide(this, enableEfficiencyMode: true);
    }

    private void CenterOnCurrentDisplay()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is null)
        {
            return;
        }

        var x = displayArea.WorkArea.X + (displayArea.WorkArea.Width - AppWindow.Size.Width) / 2;
        var y = displayArea.WorkArea.Y + (displayArea.WorkArea.Height - AppWindow.Size.Height) / 2;
        AppWindow.Move(new PointInt32(x, y));
    }

    private void ShowTrayFlyout()
    {
        _trayFlyoutWindow ??= new TrayFlyoutWindow();
        _trayFlyoutWindow.ShowNearNotificationArea();
    }
}
