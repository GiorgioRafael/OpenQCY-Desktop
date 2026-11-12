using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using OpenQCY_Desktop.Services;
using OpenQCY_Desktop.ViewModels;

namespace OpenQCY_Desktop;

public partial class App : Application
{
    public static Window Window { get; private set; } = null!;
    public static MainPageViewModel ViewModel { get; } = new();
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;
    public static bool IsExiting { get; private set; }

    public static nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(Window);

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var isStartupLaunch = WindowsStartupService.IsStartupLaunch(Environment.GetCommandLineArgs());

        // Only one instance may own the earbuds' BLE control channel: while connected, the earbuds stop
        // advertising, so a second instance could never find them. Forward the launch to the running one.
        var mainInstance = AppInstance.FindOrRegisterForKey("OpenQCY.Desktop.Main");
        if (!mainInstance.IsCurrent)
        {
            if (!isStartupLaunch)
            {
                var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
                Task.Run(() => mainInstance.RedirectActivationToAsync(activation).AsTask()).Wait();
            }

            Environment.Exit(0);
            return;
        }

        Window = new MainWindow();
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        mainInstance.Activated += (_, _) => DispatcherQueue.TryEnqueue(ShowMainWindow);

        if (isStartupLaunch && Window is MainWindow mainWindow)
        {
            mainWindow.StartHidden();
            _ = ViewModel.StartAsync();
            return;
        }

        Window.Activate();
    }

    public static void ShowMainWindow()
    {
        if (Window is MainWindow mainWindow)
        {
            mainWindow.HideTrayFlyout();
            mainWindow.ShowFromTray();
        }
    }

    public static void HideTrayFlyout()
    {
        if (Window is MainWindow mainWindow)
        {
            mainWindow.HideTrayFlyout();
        }
    }

    public static void ExitApplication()
    {
        IsExiting = true;
        ViewModel.Shutdown();
        if (Window is MainWindow mainWindow)
        {
            mainWindow.DisposeTrayIcon();
        }

        Window.Close();
    }
}
