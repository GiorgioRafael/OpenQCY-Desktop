using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using OpenQCY_Desktop.Views;
using Windows.Graphics;

namespace OpenQCY_Desktop;

public sealed class TrayFlyoutWindow : Window
{
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int WindowWidth = 420;
    private const int WindowHeight = 610;
    private bool _isClosingForExit;
    private bool _hasBeenActivated;

    public TrayFlyoutWindow()
    {
        Content = new TrayFlyout();
        AppWindow.Resize(new SizeInt32(WindowWidth, WindowHeight));
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon("Assets/AppIcon-v2.ico");

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }

        ApplyWindowChrome();

        Activated += TrayFlyoutWindow_Activated;
        AppWindow.Closing += AppWindow_Closing;
    }

    public void ShowNearNotificationArea()
    {
        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        if (displayArea is not null)
        {
            var x = displayArea.WorkArea.X + displayArea.WorkArea.Width - WindowWidth - 12;
            var y = displayArea.WorkArea.Y + displayArea.WorkArea.Height - WindowHeight - 12;
            AppWindow.Move(new PointInt32(x, y));
        }

        _hasBeenActivated = false;
        AppWindow.Show();
        Activate();
    }

    public void Hide()
    {
        AppWindow.Hide();
    }

    public void CloseForExit()
    {
        _isClosingForExit = true;
        Close();
    }

    private void TrayFlyoutWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            _hasBeenActivated = true;
            return;
        }

        if (_hasBeenActivated && !_isClosingForExit)
        {
            AppWindow.Hide();
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isClosingForExit)
        {
            return;
        }

        args.Cancel = true;
        AppWindow.Hide();
    }

    private void ApplyWindowChrome()
    {
        var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var cornerPreference = 2;
        var darkBorderColor = 0x00201711;
        _ = DwmSetWindowAttribute(windowHandle, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
        _ = DwmSetWindowAttribute(windowHandle, DwmwaBorderColor, ref darkBorderColor, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
