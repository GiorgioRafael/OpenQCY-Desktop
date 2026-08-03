using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using OpenQCY_Desktop.Views;
using Windows.Graphics;

namespace OpenQCY_Desktop;

public sealed class TrayFlyoutWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int WindowWidth = 400;
    private const int WindowHeight = 500;
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000L;
    private const long WsThickFrame = 0x00040000L;
    private const long WsMinimizeBox = 0x00020000L;
    private const long WsMaximizeBox = 0x00010000L;
    private const long WsSysMenu = 0x00080000L;
    private const long WsPopup = 0x80000000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private readonly nint _windowHandle;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _lightDismissTimer;
    private bool _isClosingForExit;
    private bool _hasBeenActivated;
    private bool _isVisible;

    public TrayFlyoutWindow()
    {
        Content = new TrayFlyout();
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
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

        _lightDismissTimer = DispatcherQueue.CreateTimer();
        _lightDismissTimer.Interval = TimeSpan.FromMilliseconds(100);
        _lightDismissTimer.IsRepeating = true;
        _lightDismissTimer.Tick += LightDismissTimer_Tick;

        Activated += TrayFlyoutWindow_Activated;
        AppWindow.Closing += AppWindow_Closing;
    }

    public bool IsVisible => _isVisible;

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
        _isVisible = true;
        AppWindow.Show();
        Activate();
        _lightDismissTimer.Start();
    }

    public void Hide()
    {
        if (!_isVisible)
        {
            return;
        }

        _isVisible = false;
        _hasBeenActivated = false;
        _lightDismissTimer.Stop();
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

        if (_isVisible && _hasBeenActivated && !_isClosingForExit)
        {
            Hide();
        }
    }

    private void LightDismissTimer_Tick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        if (!_isVisible)
        {
            sender.Stop();
            return;
        }

        if (GetForegroundWindow() == _windowHandle)
        {
            _hasBeenActivated = true;
        }
        else if (_hasBeenActivated && !_isClosingForExit)
        {
            Hide();
        }
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_isClosingForExit)
        {
            return;
        }

        args.Cancel = true;
        Hide();
    }

    private void ApplyWindowChrome()
    {
        var style = GetWindowLongPtr(_windowHandle, GwlStyle).ToInt64();
        style &= ~(WsCaption | WsThickFrame | WsMinimizeBox | WsMaximizeBox | WsSysMenu);
        style |= WsPopup;
        _ = SetWindowLongPtr(_windowHandle, GwlStyle, new nint(style));
        _ = SetWindowPos(
            _windowHandle,
            0,
            0,
            0,
            0,
            0,
            SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        var cornerPreference = 2;
        var immersiveDarkMode = 1;
        var noBorderColor = unchecked((int)0xFFFFFFFE);
        _ = DwmSetWindowAttribute(_windowHandle, DwmwaUseImmersiveDarkMode, ref immersiveDarkMode, sizeof(int));
        _ = DwmSetWindowAttribute(_windowHandle, DwmwaWindowCornerPreference, ref cornerPreference, sizeof(int));
        _ = DwmSetWindowAttribute(_windowHandle, DwmwaBorderColor, ref noBorderColor, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint windowHandle, int index, nint newValue);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
