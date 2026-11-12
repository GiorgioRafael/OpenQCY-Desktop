using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenQCY_Desktop.Bluetooth;
using OpenQCY_Desktop.Device;
using OpenQCY_Desktop.Protocol;
using OpenQCY_Desktop.Services;

namespace OpenQCY_Desktop.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private static readonly IReadOnlyDictionary<string, byte> N70EqualizerPresetIds =
        new Dictionary<string, byte>(StringComparer.Ordinal)
        {
            ["Spatial audio"] = 0,
            ["Default"] = 1,
            ["Soft stage"] = 2,
            ["Pop"] = 3,
            ["Bass boost"] = 4,
            ["Rock"] = 5,
            ["Soft"] = 6,
            ["Classical"] = 7,
        };

    private static readonly IReadOnlyDictionary<string, byte> N65EqualizerPresetIds =
        QcyN65Equalizer.Presets.ToDictionary(preset => preset.Name, preset => preset.Id, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, byte> TouchActionIds =
        new Dictionary<string, byte>(StringComparer.Ordinal)
        {
            ["No action"] = 0,
            ["Play / pause"] = 1,
            ["Previous track"] = 2,
            ["Next track"] = 3,
            ["Voice assistant"] = 4,
            ["Volume up"] = 5,
            ["Volume down"] = 6,
            ["Game mode"] = 7,
            ["Noise control"] = 8,
        };

    private static readonly IReadOnlyDictionary<string, QcyNoiseCancellationMode> NoiseCancellationModeIds =
        new Dictionary<string, QcyNoiseCancellationMode>(StringComparer.Ordinal)
        {
            ["Adaptive"] = QcyNoiseCancellationMode.Adaptive,
            ["Indoor"] = QcyNoiseCancellationMode.Indoor,
            ["Commuting"] = QcyNoiseCancellationMode.Commuting,
            ["Noisy environment"] = QcyNoiseCancellationMode.Noisy,
            ["Anti-wind"] = QcyNoiseCancellationMode.AntiWind,
        };

    private readonly ProfileStore _profileStore = new();
    private readonly WindowsStartupService _windowsStartupService = new();
    private readonly IBluetoothTransport _bluetoothTransport = new WindowsBluetoothTransport();
    private IBluetoothDeviceConnection? _connection;
    private QcyDeviceClient? _deviceClient;
    private IReadOnlyDictionary<string, byte> _equalizerPresetIds = N70EqualizerPresetIds;
    private CancellationTokenSource? _promptVolumeDebounce;
    private CancellationTokenSource? _channelBalanceDebounce;
    private CancellationTokenSource? _customEqualizerDebounce;
    private List<SavedEqualizerCurve> _savedEqualizerCurves = [];
    private CancellationTokenSource? _noiseControlCancellation;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _batteryRefreshTimer;
    private QcyNoiseControlState? _pendingNoiseControl;
    private long _noiseControlRequestVersion;
    private ulong? _knownBluetoothAddress;
    private ulong? _knownControlAddress;
    private ulong? _knownOtherAddress;
    private bool _isAggregateBatteryReading;
    private DateTimeOffset _lastBatteryRefresh = DateTimeOffset.MinValue;
    private bool _hasStarted;
    private bool _isLoading;
    private bool _isSynchronizingDevice;
    private bool _isUpdatingStartupSetting;

    public MainPageViewModel()
    {
        foreach (var gesture in N65Gestures)
        {
            gesture.Changed += N65Gesture_Changed;
        }

        LoadCustomEqualizerBands(QcyN65Equalizer.FactoryTuning.Bands);
        LoadProfile();
        LoadStartupSetting();
    }

    public IReadOnlyList<string> NoiseModes { get; } = ["Noise cancellation", "Transparency", "Normal"];
    public IReadOnlyList<string> NoiseCancellationModes { get; } = [.. NoiseCancellationModeIds.Keys];
    public IReadOnlyList<string> TouchActions { get; } = [.. TouchActionIds.Keys];
    public IReadOnlyList<string> DisconnectTimeouts { get; } = ["Never", "5 minutes", "15 minutes", "30 minutes", "1 hour"];

    [ObservableProperty]
    public partial string SelectedNoiseMode { get; set; } = "Noise cancellation";

    [ObservableProperty]
    public partial string SelectedNoiseCancellationMode { get; set; } = "Adaptive";

    public bool IsNoiseCancellationSelected => SelectedNoiseMode == "Noise cancellation";

    [ObservableProperty]
    public partial IReadOnlyList<string> EqualizerPresets { get; set; } = [.. N70EqualizerPresetIds.Keys, "Custom"];

    [ObservableProperty]
    public partial string SelectedEqualizerPreset { get; set; } = "Default";

    [ObservableProperty]
    public partial bool WearDetection { get; set; }

    [ObservableProperty]
    public partial bool LdacEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool MultipointEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool GameModeEnabled { get; set; }

    [ObservableProperty]
    public partial bool DynamicEqualizerEnabled { get; set; }

    [ObservableProperty]
    public partial bool WindReductionEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool SleepModeEnabled { get; set; }

    [ObservableProperty]
    public partial bool AutoApplyEnabled { get; set; } = true;

    [ObservableProperty]
    public partial bool RunAtStartupEnabled { get; set; }

    [ObservableProperty]
    public partial string StartupStatus { get; set; } = "Opens in the background, in the notification area only.";

    [ObservableProperty]
    public partial double PromptVolume { get; set; } = 64;

    public string PromptVolumeDisplay => $"{PromptVolume:F0}%";

    [ObservableProperty]
    public partial double ChannelBalance { get; set; } = 50;

    public string ChannelBalanceDisplay => Math.Round(ChannelBalance) switch
    {
        50 => "Centered",
        < 50 => $"L {100 - Math.Round(ChannelBalance):F0}",
        _ => $"R {Math.Round(ChannelBalance):F0}",
    };

    [ObservableProperty]
    public partial double EqBand1 { get; set; }

    [ObservableProperty]
    public partial double EqBand2 { get; set; }

    [ObservableProperty]
    public partial double EqBand3 { get; set; }

    [ObservableProperty]
    public partial double EqBand4 { get; set; }

    [ObservableProperty]
    public partial double EqBand5 { get; set; }

    [ObservableProperty]
    public partial double EqBand6 { get; set; }

    [ObservableProperty]
    public partial double EqBand7 { get; set; }

    [ObservableProperty]
    public partial double EqBand8 { get; set; }

    [ObservableProperty]
    public partial double EqBand9 { get; set; }

    [ObservableProperty]
    public partial double EqBand10 { get; set; }

    public ObservableCollection<EqualizerBandViewModel> CustomEqualizerBands { get; } = [];

    public ObservableCollection<string> SavedEqualizerCurves { get; } = [];

    public IReadOnlyList<string> FilterTypes => EqualizerBandViewModel.FilterTypes;

    // Factory functions come from the first capture, before any gesture was changed.
    public IReadOnlyList<GestureViewModel> N65Gestures { get; } =
    [
        new("Single tap", true, 0x01, 0x06),
        new("Single tap", false, 0x02, 0x05),
        new("Double tap", true, 0x03, 0x01),
        new("Double tap", false, 0x04, 0x03),
        new("Triple tap", true, 0x05, 0x02),
        new("Triple tap", false, 0x06, 0x0B),
        new("Press and hold", true, 0x09, 0x04),
        new("Press and hold", false, 0x0A, 0x04),
    ];

    [ObservableProperty]
    public partial bool IsLeftEarbudSelected { get; set; } = true;

    public bool IsRightEarbudSelected => !IsLeftEarbudSelected;

    public IReadOnlyList<GestureViewModel> SelectedEarbudGestures =>
        N65Gestures.Where(gesture => gesture.IsLeft == IsLeftEarbudSelected).ToArray();

    [ObservableProperty]
    public partial double CustomEqualizerBandCount { get; set; }

    [ObservableProperty]
    public partial string? SelectedSavedEqualizerCurve { get; set; }

    [ObservableProperty]
    public partial string EqualizerCurveName { get; set; } = "";







    [ObservableProperty]
    public partial string LeftDoubleTap { get; set; } = "Play / pause";

    [ObservableProperty]
    public partial string RightDoubleTap { get; set; } = "Next track";

    [ObservableProperty]
    public partial string LeftLongPress { get; set; } = "Noise control";

    [ObservableProperty]
    public partial string RightLongPress { get; set; } = "Voice assistant";

    [ObservableProperty]
    public partial string DisconnectTimeout { get; set; } = "Never";

    [ObservableProperty]
    public partial string SaveStatus { get; set; } = "Local preferences loaded";

    [ObservableProperty]
    public partial string DiscoveryStatus { get; set; } = "Ready to find the QCY control channel";

    [ObservableProperty]
    public partial string DeviceName { get; set; } = "QCY MeloBuds";

    [ObservableProperty]
    public partial string ConnectionStatus { get; set; } = "Control disconnected";

    [ObservableProperty]
    public partial string FirmwareVersion { get; set; } = "—";

    [ObservableProperty]
    public partial string LeftBatteryDisplay { get; set; } = "—";

    [ObservableProperty]
    public partial string RightBatteryDisplay { get; set; } = "—";

    [ObservableProperty]
    public partial string CaseBatteryDisplay { get; set; } = "—";

    [ObservableProperty]
    public partial string BatteryStatus { get; set; } = "Looking for a recent battery reading";

    [ObservableProperty]
    public partial double LeftBattery { get; set; }

    [ObservableProperty]
    public partial double RightBattery { get; set; }

    [ObservableProperty]
    public partial double CaseBattery { get; set; }

    [ObservableProperty]
    public partial bool IsDeviceConnected { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool WearDetectionSupported { get; set; }

    [ObservableProperty]
    public partial bool EqualizerSupported { get; set; }

    [ObservableProperty]
    public partial bool EqualizerPresetsSupported { get; set; }

    [ObservableProperty]
    public partial string EqualizerCapabilityStatus { get; set; } = "Availability checked on connection";

    [ObservableProperty]
    public partial bool KeyFunctionsSupported { get; set; }

    [ObservableProperty]
    public partial bool IsN65Model { get; set; }

    public bool IsN70Model => !IsN65Model;

    public string ModelLabel => IsN65Model ? "MeloBuds N65" : "MeloBuds N70";

    public bool ShowCancellationModes => IsNoiseCancellationSelected && !IsN65Model;

    public async Task StartAsync()
    {
        if (_hasStarted)
        {
            return;
        }

        _hasStarted = true;
        _batteryRefreshTimer = App.DispatcherQueue.CreateTimer();
        _batteryRefreshTimer.Interval = TimeSpan.FromSeconds(30);
        _batteryRefreshTimer.IsRepeating = true;
        _batteryRefreshTimer.Tick += BatteryRefreshTimer_Tick;
        _batteryRefreshTimer.Start();
        await DiscoverDevicesAsync();
    }

    [RelayCommand]
    private async Task DiscoverDevicesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        var desiredProfile = CreateProfile();
        BluetoothBatteryInfo? windowsBattery = null;
        try
        {
            await DisconnectCoreAsync(updateStatus: false);
            windowsBattery = await _bluetoothTransport.FindWindowsBatteryAsync();
            if (windowsBattery is not null)
            {
                DeviceName = windowsBattery.DeviceName;
                LeftBattery = windowsBattery.Percentage;
                RightBattery = windowsBattery.Percentage;
                CaseBattery = 0;
                _isAggregateBatteryReading = true;
                UpdateBatteryDisplays();
                BatteryStatus = windowsBattery.IsConnected
                    ? "Overall estimate provided by Windows; connecting to read each side"
                    : "Last overall estimate stored by Windows";
            }

            DiscoveryStatus = "Looking for the earbuds already known to Windows…";
            ConnectionStatus = "Looking for the saved control channel";

            var knownDevices = (await _bluetoothTransport.FindPairedQcyDevicesAsync())
                .Where(device => QcyUuids.IsSupported(device.VendorId))
                .ToList();
            var rememberedDevice = CreateRememberedDevice(desiredProfile);
            if (rememberedDevice is not null)
            {
                knownDevices.Insert(0, rememberedDevice);
            }

            var target = await ConnectFirstAvailableAsync(knownDevices);
            if (target is null)
            {
                DiscoveryStatus = "The earbuds were not cached; scanning for BLE advertisements…";
                ConnectionStatus = "Looking for the control channel advertisement";
                var advertisedDevices = await _bluetoothTransport.ScanForQcyDevicesAsync(TimeSpan.FromSeconds(8));
                var advertisedSupported = advertisedDevices
                    .Where(device => QcyUuids.IsSupported(device.VendorId))
                    .ToArray();
                target = await ConnectFirstAvailableAsync(advertisedSupported);
                if (target is null)
                {
                    throw new InvalidOperationException(
                        "The earbuds were not found in the Windows cache and did not advertise their control channel. Open the case or remove an earbud and try again.");
                }
            }

            DeviceName = target.Name;
            var isFirstConnection = desiredProfile.KnownBluetoothAddress is null &&
                desiredProfile.KnownControlAddress is null &&
                desiredProfile.KnownOtherAddress is null;
            var modelChanged = _deviceClient!.IsJieli != desiredProfile.IsN65Model;
            SetModel(_deviceClient.IsJieli);
            RememberDevice(target);
            if (target.SignalStrength != short.MinValue)
            {
                _isAggregateBatteryReading = false;
                LeftBattery = target.LeftBattery;
                RightBattery = target.RightBattery;
                CaseBattery = target.CaseBattery;
                UpdateBatteryDisplays();
                BatteryStatus = "Battery detected from the earbuds' advertisement";
            }

            ApplyDeviceState(_deviceClient!.State);

            // On the first connection, or with a different model, keep the earbuds' settings instead of
            // overwriting them with the defaults.
            var shouldReapply = desiredProfile.AutoApplyEnabled && !isFirstConnection && !modelChanged;
            if (shouldReapply)
            {
                SaveStatus = "Reapplying the saved profile to the earbuds…";
                await ApplyProfileToDeviceAsync(desiredProfile);
            }

            ApplyDeviceState(_deviceClient.State);
            DiscoveryStatus = "QCY control channel connected and validated";
            BatteryStatus = "Updates automatically while the control channel is connected";
            Save();
            SaveStatus = shouldReapply
                ? "Profile confirmed by the earbuds"
                : "Current earbud state loaded";
        }
        catch (Exception exception)
        {
            await DisconnectCoreAsync(updateStatus: false);
            ConnectionStatus = "Control disconnected";
            DiscoveryStatus = FriendlyError(exception);
            SaveStatus = "Your preferences remain saved on this PC";
            BatteryStatus = windowsBattery is not null
                ? "Overall Windows estimate; open the case to read each earbud and the case"
                : HasBatteryReading
                ? "Showing the last reading received from the earbuds"
                : "No reading — open the case or remove an earbud to advertise the battery";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DisconnectDeviceAsync()
    {
        await DisconnectCoreAsync(updateStatus: true);
    }

    [RelayCommand]
    private async Task ApplyProfileAsync()
    {
        Save();
        if (_deviceClient is null || !_deviceClient.State.IsConnected)
        {
            SaveStatus = "Profile saved — connect the earbuds to apply it";
            return;
        }

        await RunDeviceActionAsync(
            client => ApplyProfileToDeviceAsync(CreateProfile()),
            "Profile applied and confirmed by the earbuds");
    }

    [RelayCommand]
    private async Task ApplyEqualizerAsync()
    {
        if (_deviceClient is null || !_deviceClient.State.IsConnected)
        {
            SaveStatus = "Equalizer saved — connect the earbuds to apply it";
            return;
        }

        if (SelectedEqualizerPreset == "Custom" && IsN65Model)
        {
            await RunDeviceActionAsync(
                client => client.SetN65EqualizerAsync(GetCustomEqualizerBands()),
                "Custom equalizer confirmed by the earbuds");
            return;
        }

        if (SelectedEqualizerPreset == "Custom")
        {
            if (!EqualizerSupported)
            {
                SaveStatus = "This firmware does not expose a compatible equalizer channel";
                return;
            }

            await RunDeviceActionAsync(
                client => client.SetCustomEqualizerAsync(GetEqualizerBands()),
                "Custom equalizer confirmed by the earbuds");
            return;
        }

        if (!EqualizerPresetsSupported)
        {
            SaveStatus = "Firmware 3.0.13 supports the custom curve but does not expose the legacy preset channel";
            return;
        }

        await RunDeviceActionAsync(
            client => client.SetEqualizerPresetAsync(_equalizerPresetIds[SelectedEqualizerPreset]),
            "Preset confirmed by the earbuds");
    }

    [RelayCommand]
    private void SaveProfile()
    {
        Save();
        SaveStatus = "Preferences saved on this PC";
    }

    [RelayCommand]
    private void SaveEqualizerCurve()
    {
        var name = EqualizerCurveName.Trim();
        if (name.Length == 0)
        {
            SaveStatus = "Type a name to save the curve";
            return;
        }

        _savedEqualizerCurves.RemoveAll(curve => curve.Name == name);
        _savedEqualizerCurves.Add(new SavedEqualizerCurve { Name = name, Bands = ToSavedBands(GetCustomEqualizerBands()) });
        _savedEqualizerCurves.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.CurrentCulture));
        RefreshSavedEqualizerCurves(name);
        Save();
        SaveStatus = $"Curve \"{name}\" saved on this PC";
    }

    [RelayCommand]
    private void DeleteEqualizerCurve()
    {
        var name = SelectedSavedEqualizerCurve;
        if (name is null || _savedEqualizerCurves.RemoveAll(curve => curve.Name == name) == 0)
        {
            return;
        }

        RefreshSavedEqualizerCurves(null);
        Save();
        SaveStatus = $"Curve \"{name}\" deleted";
    }

    [RelayCommand]
    private void EditSelectedPreset()
    {
        var preset = _equalizerPresetIds.TryGetValue(SelectedEqualizerPreset, out var id)
            ? QcyN65Equalizer.GetPreset(id)
            : null;
        if (preset is null)
        {
            return;
        }

        LoadCustomEqualizerBands(preset.Bands);
        QueueCustomEqualizerChange();
    }

    [RelayCommand]
    private void SelectLeftEarbud() => IsLeftEarbudSelected = true;

    [RelayCommand]
    private void SelectRightEarbud() => IsLeftEarbudSelected = false;

    [RelayCommand]
    private void RestoreFactoryGestures()
    {
        _isSynchronizingDevice = true;
        try
        {
            foreach (var gesture in N65Gestures)
            {
                gesture.Action = GestureViewModel.FromFunction(gesture.FactoryFunction);
            }
        }
        finally
        {
            _isSynchronizingDevice = false;
        }

        PersistAndRun(
            async client =>
            {
                foreach (var gesture in N65Gestures)
                {
                    await client.SetN65KeyFunctionAsync(gesture.Gesture, gesture.FactoryFunction);
                }
            },
            "Factory gestures restored on the earbuds");
    }

    [RelayCommand]
    private void ResetEqualizer()
    {
        if (IsN65Model)
        {
            LoadCustomEqualizerBands(QcyN65Equalizer.FactoryTuning.Bands);
            QueueCustomEqualizerChange();
            SaveStatus = "Custom curve reset to the factory tuning";
            return;
        }

        _isSynchronizingDevice = true;
        try
        {
            EqBand1 = 0;
            EqBand2 = 0;
            EqBand3 = 0;
            EqBand4 = 0;
            EqBand5 = 0;
            EqBand6 = 0;
            EqBand7 = 0;
            EqBand8 = 0;
            EqBand9 = 0;
            EqBand10 = 0;
            SelectedEqualizerPreset = "Custom";
        }
        finally
        {
            _isSynchronizingDevice = false;
        }

        Save();
        SaveStatus = "Equalizer reset — click Apply";
    }

    public void Shutdown()
    {
        _batteryRefreshTimer?.Stop();
        _ = DisconnectCoreAsync(updateStatus: false);
    }

    partial void OnSelectedNoiseModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsNoiseCancellationSelected));
        OnPropertyChanged(nameof(ShowCancellationModes));
        if (value is null || !NoiseModes.Contains(value, StringComparer.Ordinal) ||
            _isLoading || _isSynchronizingDevice)
        {
            return;
        }

        Save();
        QueueNoiseControlChange();
    }

    partial void OnSelectedNoiseCancellationModeChanged(string value)
    {
        if (value is null || !NoiseCancellationModeIds.ContainsKey(value) ||
            _isLoading || _isSynchronizingDevice)
        {
            return;
        }

        Save();
        if (IsNoiseCancellationSelected)
        {
            QueueNoiseControlChange();
        }
    }

    partial void OnSelectedEqualizerPresetChanged(string value)
    {
        Save();
        if (value != "Custom" && EqualizerPresetsSupported &&
            _equalizerPresetIds.TryGetValue(value, out var preset))
        {
            RunWhenConnected(client => client.SetEqualizerPresetAsync(preset), "Equalizer confirmed by the earbuds");
        }
        else if (value == "Custom" && IsN65Model)
        {
            RunWhenConnected(
                client => client.SetN65EqualizerAsync(GetCustomEqualizerBands()),
                "Custom equalizer confirmed by the earbuds");
        }
    }

    partial void OnCustomEqualizerBandCountChanged(double value)
    {
        if (double.IsNaN(value) || _isLoading || _isSynchronizingDevice)
        {
            return;
        }

        var count = (int)Math.Clamp(Math.Round(value), 1, QcyN65Equalizer.MaximumBands);
        if (count == CustomEqualizerBands.Count)
        {
            return;
        }

        var bands = GetCustomEqualizerBands().Take(count).ToList();
        while (bands.Count < count)
        {
            bands.Add(QcyN65Equalizer.NewBand(bands));
        }

        LoadCustomEqualizerBands(bands);
        QueueCustomEqualizerChange();
    }

    partial void OnSelectedSavedEqualizerCurveChanged(string? value)
    {
        var curve = _savedEqualizerCurves.FirstOrDefault(item => item.Name == value);
        if (curve is null || _isLoading)
        {
            return;
        }

        EqualizerCurveName = curve.Name;
        LoadCustomEqualizerBands(FromSavedBands(curve.Bands));
        QueueCustomEqualizerChange();
    }

    partial void OnWearDetectionChanged(bool value) => PersistAndRun(
        client => client.SetWearDetectionAsync(value),
        value ? "Wear detection enabled on the earbuds" : "Wear detection disabled on the earbuds");

    partial void OnLdacEnabledChanged(bool value) => PersistAndRun(
        client => client.SetLdacAsync(value),
        IsN65Model
            ? "LDAC sent; the earbuds restart and OpenQCY reconnects in a few seconds"
            : "LDAC sent; the earbuds may restart the connection");

    partial void OnMultipointEnabledChanged(bool value) => PersistAndRun(
        client => client.SetMultipointAsync(value),
        "Multipoint sent; the earbuds may restart the connection");

    partial void OnGameModeEnabledChanged(bool value) => PersistAndRun(
        client => client.SetGameModeAsync(value),
        IsN65Model
            ? "Game mode sent; the earbuds restart and OpenQCY reconnects in a few seconds"
            : "Game mode confirmed by the earbuds");

    partial void OnDynamicEqualizerEnabledChanged(bool value)
    {
        if (value && IsN65Model && SelectedEqualizerPreset == "Custom" && CanSendToDevice)
        {
            SaveStatus = "Dynamic EQ only works with a preset, not with the custom curve";
            App.DispatcherQueue.TryEnqueue(() =>
            {
                _isSynchronizingDevice = true;
                try
                {
                    DynamicEqualizerEnabled = false;
                }
                finally
                {
                    _isSynchronizingDevice = false;
                }
            });
            return;
        }

        PersistAndRun(
            client => client.SetDynamicEqualizerAsync(value),
            value ? "Dynamic EQ enabled on the earbuds" : "Dynamic EQ disabled on the earbuds");
    }

    partial void OnChannelBalanceChanged(double value)
    {
        OnPropertyChanged(nameof(ChannelBalanceDisplay));
        Save();
        if (!CanSendToDevice || !IsN65Model)
        {
            return;
        }

        _channelBalanceDebounce?.Cancel();
        _channelBalanceDebounce?.Dispose();
        _channelBalanceDebounce = new CancellationTokenSource();
        _ = SendChannelBalanceAfterDelayAsync(value, _channelBalanceDebounce.Token);
    }

    partial void OnIsLeftEarbudSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRightEarbudSelected));
        OnPropertyChanged(nameof(SelectedEarbudGestures));
    }

    partial void OnIsN65ModelChanged(bool value)
    {
        OnPropertyChanged(nameof(IsN70Model));
        OnPropertyChanged(nameof(ModelLabel));
        OnPropertyChanged(nameof(ShowCancellationModes));
    }

    partial void OnWindReductionEnabledChanged(bool value) => PersistAndRun(
        client => client.SetWindDetectionAsync(value),
        "Wind detection confirmed by the earbuds");

    partial void OnSleepModeEnabledChanged(bool value) => PersistAndRun(
        client => client.SetSleepModeAsync(value),
        "Sleep mode confirmed by the earbuds");

    partial void OnAutoApplyEnabledChanged(bool value) => Save();

    partial void OnRunAtStartupEnabledChanged(bool value)
    {
        if (_isUpdatingStartupSetting)
        {
            return;
        }

        try
        {
            _windowsStartupService.SetEnabled(value);
            StartupStatus = value
                ? "Will start in the background, in the notification area only."
                : "Will not start automatically with Windows.";
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            _isUpdatingStartupSetting = true;
            try
            {
                RunAtStartupEnabled = !value;
            }
            finally
            {
                _isUpdatingStartupSetting = false;
            }

            StartupStatus = "Could not change the startup setting.";
        }
    }

    partial void OnPromptVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(PromptVolumeDisplay));
        Save();
        if (!CanSendToDevice)
        {
            return;
        }

        _promptVolumeDebounce?.Cancel();
        _promptVolumeDebounce?.Dispose();
        _promptVolumeDebounce = new CancellationTokenSource();
        _ = SendPromptVolumeAfterDelayAsync(value, _promptVolumeDebounce.Token);
    }

    partial void OnEqBand1Changed(double value) => PersistEqualizerBand();
    partial void OnEqBand2Changed(double value) => PersistEqualizerBand();
    partial void OnEqBand3Changed(double value) => PersistEqualizerBand();
    partial void OnEqBand4Changed(double value) => PersistEqualizerBand();
    partial void OnEqBand5Changed(double value) => PersistEqualizerBand();
    partial void OnEqBand6Changed(double value) => PersistEqualizerBand();
    partial void OnEqBand7Changed(double value) => PersistEqualizerBand();
    partial void OnEqBand8Changed(double value) => PersistEqualizerBand();
    partial void OnEqBand9Changed(double value) => PersistEqualizerBand();
    partial void OnEqBand10Changed(double value) => PersistEqualizerBand();

    partial void OnLeftDoubleTapChanged(string value) => PersistTouchFunctions();
    partial void OnRightDoubleTapChanged(string value) => PersistTouchFunctions();
    partial void OnLeftLongPressChanged(string value) => PersistTouchFunctions();
    partial void OnRightLongPressChanged(string value) => PersistTouchFunctions();

    partial void OnDisconnectTimeoutChanged(string value) => PersistAndRun(
        client => client.SetAutoPowerOffAsync(ToAutoPowerOffMinutes(value)),
        "Automatic power off confirmed by the earbuds");

    private async Task ApplyProfileToDeviceAsync(DeviceProfile profile)
    {
        var client = _deviceClient ?? throw new InvalidOperationException("The earbuds are not connected.");
        var state = client.State;

        if (state.WearDetectionEnabled.HasValue && state.WearDetectionEnabled != profile.WearDetection)
        {
            await client.SetWearDetectionAsync(profile.WearDetection);
        }

        var noiseControl = ToNoiseControl(profile.SelectedNoiseMode, profile.SelectedNoiseCancellationMode, IsN65Model);
        if (state.NoiseControl is not null && state.NoiseControl != noiseControl)
        {
            await client.SetNoiseModeAsync(
                noiseControl.Mode,
                noiseControl.CancellationMode ?? QcyNoiseCancellationMode.Adaptive);
        }

        if (client.IsJieli)
        {
            await ApplyN65ProfileAsync(client, profile);
            return;
        }

        if (state.GameModeEnabled.HasValue && state.GameModeEnabled != profile.GameModeEnabled)
        {
            await client.SetGameModeAsync(profile.GameModeEnabled);
        }

        if (state.SleepModeEnabled.HasValue && state.SleepModeEnabled != profile.SleepModeEnabled)
        {
            await client.SetSleepModeAsync(profile.SleepModeEnabled);
        }

        if (state.WindDetectionEnabled.HasValue && state.WindDetectionEnabled != profile.WindReductionEnabled)
        {
            await client.SetWindDetectionAsync(profile.WindReductionEnabled);
        }

        if (state.PromptVolume.HasValue)
        {
            var currentPercentage = state.PromptVolume.Value * 100d / state.PromptVolumeMaximum;
            if (Math.Abs(currentPercentage - profile.PromptVolume) >= 1)
            {
                await client.SetPromptVolumeAsync(profile.PromptVolume);
            }
        }

        var autoPowerOff = ToAutoPowerOffMinutes(profile.DisconnectTimeout);
        if (state.AutoPowerOffMinutes.HasValue && state.AutoPowerOffMinutes != autoPowerOff)
        {
            await client.SetAutoPowerOffAsync(autoPowerOff);
        }

        if (profile.SelectedEqualizerPreset == "Custom" && EqualizerSupported)
        {
            await client.SetCustomEqualizerAsync(profile.EqBands);
        }
        else if (EqualizerPresetsSupported &&
            _equalizerPresetIds.TryGetValue(profile.SelectedEqualizerPreset, out var preset) &&
            state.EqualizerPreset != preset)
        {
            await client.SetEqualizerPresetAsync(preset);
        }

        if (KeyFunctionsSupported && state.KeyFunctions.Count > 0)
        {
            await client.SetKeyFunctionsAsync(BuildKeyFunctionMap(profile, state.KeyFunctions));
        }

        // These two settings can restart the Bluetooth link, so they are deliberately last.
        if (state.LdacEnabled.HasValue && state.LdacEnabled != profile.LdacEnabled)
        {
            await client.SetLdacAsync(profile.LdacEnabled);
        }

        if (client.State.IsConnected && state.MultipointEnabled.HasValue &&
            state.MultipointEnabled != profile.MultipointEnabled)
        {
            await client.SetMultipointAsync(profile.MultipointEnabled);
        }
    }

    private async Task ApplyN65ProfileAsync(QcyDeviceClient client, DeviceProfile profile)
    {
        var state = client.State;
        if (profile.SelectedEqualizerPreset == "Custom")
        {
            var desired = FromSavedBands(profile.N65CustomBands);
            if (desired.Count > 0 &&
                (state.EqualizerPreset != QcyN65Equalizer.CustomPreset || !HasSameBands(state.Equalizer, desired)))
            {
                await client.SetN65EqualizerAsync(desired);
            }
        }
        else if (N65EqualizerPresetIds.TryGetValue(profile.SelectedEqualizerPreset, out var preset) &&
            state.EqualizerPreset != preset)
        {
            await client.SetEqualizerPresetAsync(preset);
        }

        // A preset change turns dynamic EQ off on the earbuds, so it is applied afterwards.
        state = client.State;
        if (profile.SelectedEqualizerPreset != "Custom" && state.DynamicEqualizerEnabled.HasValue &&
            state.DynamicEqualizerEnabled != profile.DynamicEqualizerEnabled)
        {
            await client.SetDynamicEqualizerAsync(profile.DynamicEqualizerEnabled);
        }

        var balance = (byte)Math.Clamp(Math.Round(profile.ChannelBalance), 0, 100);
        if (state.ChannelBalance.HasValue && state.ChannelBalance != balance)
        {
            await client.SetChannelBalanceAsync(balance);
        }

        foreach (var gesture in N65Gestures)
        {
            if (profile.N65Gestures.TryGetValue(gesture.Name, out var action) &&
                GestureViewModel.ToFunction(action) is { } function &&
                state.KeyFunctions.TryGetValue(gesture.Gesture, out var current) &&
                current != function)
            {
                await client.SetN65KeyFunctionAsync(gesture.Gesture, function);
            }
        }

        // LDAC and game mode restart the N65 link, so only one of them is sent per connection.
        // The other one is applied after the reconnection.
        if (state.LdacEnabled.HasValue && state.LdacEnabled != profile.LdacEnabled)
        {
            await client.SetLdacAsync(profile.LdacEnabled);
        }
        else if (state.GameModeEnabled.HasValue && state.GameModeEnabled != profile.GameModeEnabled)
        {
            await client.SetGameModeAsync(profile.GameModeEnabled);
        }
    }

    private void DeviceClient_StateChanged(object? sender, QcyDeviceState state)
    {
        if (App.DispatcherQueue.HasThreadAccess)
        {
            HandleDeviceState(state);
        }
        else
        {
            App.DispatcherQueue.TryEnqueue(() => HandleDeviceState(state));
        }
    }

    private void HandleDeviceState(QcyDeviceState state)
    {
        var wasConnected = IsDeviceConnected;
        ApplyDeviceState(state);
        if (wasConnected && !state.IsConnected && state.ProtocolVariant == QcyProtocolVariant.Jieli)
        {
            // LDAC and game mode restart the N65 link. Reconnect without waiting for the battery timer.
            DiscoveryStatus = "The earbuds restarted the connection; reconnecting…";
            _ = ReconnectAfterDelayAsync();
        }
    }

    private async Task ReconnectAfterDelayAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(6));
        if (_deviceClient?.State.IsConnected != true)
        {
            await DiscoverDevicesAsync();
        }
    }

    private void ApplyDeviceState(QcyDeviceState state)
    {
        _isSynchronizingDevice = true;
        try
        {
            IsDeviceConnected = state.IsConnected;
            ConnectionStatus = state.IsConnected ? "QCY control connected" : "Control disconnected";
            DeviceName = state.DeviceName;
            FirmwareVersion = state.FirmwareVersion ?? "—";
            if (state.Battery.Left.HasValue || state.Battery.Right.HasValue || state.Battery.Case.HasValue)
            {
                _isAggregateBatteryReading = false;
            }

            LeftBattery = state.Battery.Left ?? LeftBattery;
            RightBattery = state.Battery.Right ?? RightBattery;
            CaseBattery = state.Battery.Case ?? CaseBattery;
            UpdateBatteryDisplays();
            if (state.Battery.Left.HasValue || state.Battery.Right.HasValue || state.Battery.Case.HasValue)
            {
                BatteryStatus = "Updates automatically while the control channel is connected";
            }

            WearDetectionSupported = state.WearDetectionProtocol != QcyWearDetectionProtocol.Unknown;

            if (state.WearDetectionEnabled.HasValue)
            {
                WearDetection = state.WearDetectionEnabled.Value;
            }

            if (state.NoiseControl is not null &&
                (_pendingNoiseControl is null || state.NoiseControl == _pendingNoiseControl))
            {
                if (state.NoiseControl.CancellationMode.HasValue)
                {
                    SelectedNoiseCancellationMode = FromNoiseCancellationMode(
                        state.NoiseControl.CancellationMode.Value);
                }

                SelectedNoiseMode = FromNoiseMode(state.NoiseControl.Mode);
            }

            GameModeEnabled = state.GameModeEnabled ?? GameModeEnabled;
            DynamicEqualizerEnabled = state.DynamicEqualizerEnabled ?? DynamicEqualizerEnabled;
            ChannelBalance = state.ChannelBalance ?? ChannelBalance;
            SleepModeEnabled = state.SleepModeEnabled ?? SleepModeEnabled;
            LdacEnabled = state.LdacEnabled ?? LdacEnabled;
            MultipointEnabled = state.MultipointEnabled ?? MultipointEnabled;
            WindReductionEnabled = state.WindDetectionEnabled ?? WindReductionEnabled;

            if (state.PromptVolume.HasValue && state.PromptVolumeMaximum > 0)
            {
                PromptVolume = state.PromptVolume.Value * 100d / state.PromptVolumeMaximum;
            }

            if (state.AutoPowerOffMinutes.HasValue)
            {
                DisconnectTimeout = FromAutoPowerOffMinutes(state.AutoPowerOffMinutes.Value);
            }

            if (state.ProtocolVariant == QcyProtocolVariant.Jieli)
            {
                ApplyN65Equalizer(state);
                foreach (var gesture in N65Gestures)
                {
                    if (state.KeyFunctions.TryGetValue(gesture.Gesture, out var function))
                    {
                        gesture.Action = GestureViewModel.FromFunction(function);
                    }
                }
            }
            else if (state.EqualizerPreset.HasValue)
            {
                SelectedEqualizerPreset = _equalizerPresetIds
                    .FirstOrDefault(pair => pair.Value == state.EqualizerPreset.Value).Key ?? SelectedEqualizerPreset;
            }

            if (state.ProtocolVariant == QcyProtocolVariant.Standard && state.EqualizerGains.Count == 10)
            {
                EqBand1 = state.EqualizerGains[0];
                EqBand2 = state.EqualizerGains[1];
                EqBand3 = state.EqualizerGains[2];
                EqBand4 = state.EqualizerGains[3];
                EqBand5 = state.EqualizerGains[4];
                EqBand6 = state.EqualizerGains[5];
                EqBand7 = state.EqualizerGains[6];
                EqBand8 = state.EqualizerGains[7];
                EqBand9 = state.EqualizerGains[8];
                EqBand10 = state.EqualizerGains[9];
            }

            if (state.KeyFunctions.Count > 0)
            {
                LeftDoubleTap = FromTouchAction(state.KeyFunctions.GetValueOrDefault((byte)0x03));
                RightDoubleTap = FromTouchAction(state.KeyFunctions.GetValueOrDefault((byte)0x04));
                LeftLongPress = FromTouchAction(state.KeyFunctions.GetValueOrDefault((byte)0x09));
                RightLongPress = FromTouchAction(state.KeyFunctions.GetValueOrDefault((byte)0x0A));
            }
        }
        finally
        {
            _isSynchronizingDevice = false;
        }

        // Responses can arrive after the initial refresh timeout. Re-evaluate the
        // capabilities on every state update so a late equalizer response is not
        // left disabled for the remainder of the connection.
        UpdateCapabilities(state);
    }

    private void ApplyN65Equalizer(QcyDeviceState state)
    {
        if (!state.EqualizerPreset.HasValue)
        {
            return;
        }

        if (state.EqualizerPreset == QcyN65Equalizer.CustomPreset)
        {
            SelectedEqualizerPreset = "Custom";

            // Reloading the editor while the user drags a slider would replace the controls,
            // so only reload when the earbuds report a different curve.
            if (state.Equalizer is not null && !HasSameBands(state.Equalizer, GetCustomEqualizerBands()))
            {
                LoadCustomEqualizerBands(state.Equalizer.Bands);
            }

            return;
        }

        SelectedEqualizerPreset = _equalizerPresetIds
            .FirstOrDefault(pair => pair.Value == state.EqualizerPreset.Value).Key ?? SelectedEqualizerPreset;
    }

    private void SetModel(bool isN65)
    {
        IsN65Model = isN65;
        _equalizerPresetIds = isN65 ? N65EqualizerPresetIds : N70EqualizerPresetIds;
        var presets = new List<string>(_equalizerPresetIds.Keys) { "Custom" };
        var selected = SelectedEqualizerPreset;
        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            if (!EqualizerPresets.SequenceEqual(presets))
            {
                EqualizerPresets = presets;
            }

            SelectedEqualizerPreset = presets.Contains(selected) ? selected : "Default";
        }
        finally
        {
            _isLoading = wasLoading;
        }
    }

    private void UpdateCapabilities(QcyDeviceState state)
    {
        var characteristics = _connection?.Characteristics ?? [];
        var isJieli = state.ProtocolVariant == QcyProtocolVariant.Jieli;
        var commandUuid = isJieli ? QcyUuids.JieliCommand : QcyUuids.Command;
        var notificationUuid = isJieli ? QcyUuids.JieliNotification : QcyUuids.Notification;
        var commandChannelSupported = state.IsConnected &&
            characteristics.Any(info => info.Uuid == commandUuid && info.CanWrite) &&
            characteristics.Any(info => info.Uuid == notificationUuid && info.CanNotify);
        if (isJieli)
        {
            EqualizerSupported = commandChannelSupported;
            EqualizerPresetsSupported = commandChannelSupported;
            EqualizerCapabilityStatus = !state.IsConnected
                ? "Availability checked on connection"
                : "Presets from the official app · Custom adjusts the factory tuning (0 dB = default sound)";
            KeyFunctionsSupported = commandChannelSupported && state.KeyFunctions.Count > 0;
            WearDetectionSupported = false;
            return;
        }

        EqualizerPresetsSupported = state.IsConnected &&
            characteristics.Any(info => info.Uuid == QcyUuids.Equalizer && info.CanWrite);
        var customEqualizerConfirmed = state.IsConnected && state.EqualizerGains.Count == 10;

        // The custom ten-band EQ is sent through the validated command/notification
        // channel, not through the optional legacy preset characteristic (000B).
        // The write path still requires a device response before reporting success.
        EqualizerSupported = commandChannelSupported;
        EqualizerCapabilityStatus = !state.IsConnected
            ? "Availability checked on connection"
            : EqualizerPresetsSupported
            ? "Presets and custom curve available"
            : customEqualizerConfirmed
                ? "Custom curve available · presets not exposed by the firmware"
                : commandChannelSupported
                    ? "Custom curve available through the control channel · presets not exposed by the firmware"
                    : "Equalizer control channel not found";
        KeyFunctionsSupported = state.IsConnected &&
            characteristics.Any(info => info.Uuid == QcyUuids.KeyFunctions && info.CanWrite);
        WearDetectionSupported = state.IsConnected &&
            state.WearDetectionProtocol != QcyWearDetectionProtocol.Unknown;
    }

    private void N65Gesture_Changed(object? sender, EventArgs eventArgs)
    {
        if (sender is not GestureViewModel { Function: { } function } gesture ||
            _isLoading || _isSynchronizingDevice)
        {
            return;
        }

        PersistAndRun(
            client => client.SetN65KeyFunctionAsync(gesture.Gesture, function),
            $"{gesture.Name} confirmed by the earbuds");
    }

    private void PersistTouchFunctions()
    {
        Save();
        RunWhenConnected(
            client => client.SetKeyFunctionsAsync(BuildKeyFunctionMap(CreateProfile(), client.State.KeyFunctions)),
            "Gestures confirmed by the earbuds");
    }

    private void LoadCustomEqualizerBands(IEnumerable<QcyEqualizerBand> bands)
    {
        var wasSynchronizing = _isSynchronizingDevice;
        _isSynchronizingDevice = true;
        try
        {
            foreach (var band in CustomEqualizerBands)
            {
                band.Changed -= CustomEqualizerBand_Changed;
            }

            CustomEqualizerBands.Clear();
            foreach (var band in bands.Take(QcyN65Equalizer.MaximumBands))
            {
                var item = new EqualizerBandViewModel(CustomEqualizerBands.Count + 1, band);
                item.Changed += CustomEqualizerBand_Changed;
                CustomEqualizerBands.Add(item);
            }

            CustomEqualizerBandCount = CustomEqualizerBands.Count;
        }
        finally
        {
            _isSynchronizingDevice = wasSynchronizing;
        }
    }

    private void CustomEqualizerBand_Changed(object? sender, EventArgs eventArgs)
    {
        if (!_isSynchronizingDevice)
        {
            QueueCustomEqualizerChange();
        }
    }

    private void QueueCustomEqualizerChange()
    {
        if (_isLoading)
        {
            return;
        }

        // The official app sends every change right away; the debounce only merges slider drags.
        if (SelectedEqualizerPreset != "Custom")
        {
            _isSynchronizingDevice = true;
            try
            {
                SelectedEqualizerPreset = "Custom";
            }
            finally
            {
                _isSynchronizingDevice = false;
            }
        }

        Save();
        if (!CanSendToDevice || !IsN65Model)
        {
            return;
        }

        _customEqualizerDebounce?.Cancel();
        _customEqualizerDebounce?.Dispose();
        _customEqualizerDebounce = new CancellationTokenSource();
        _ = SendCustomEqualizerAfterDelayAsync(_customEqualizerDebounce.Token);
    }

    private IReadOnlyList<QcyEqualizerBand> GetCustomEqualizerBands() =>
        CustomEqualizerBands.Select(band => band.ToBand()).ToArray();

    private void RefreshSavedEqualizerCurves(string? selected)
    {
        var wasLoading = _isLoading;
        _isLoading = true;
        try
        {
            SavedEqualizerCurves.Clear();
            foreach (var curve in _savedEqualizerCurves)
            {
                SavedEqualizerCurves.Add(curve.Name);
            }

            SelectedSavedEqualizerCurve = selected;
        }
        finally
        {
            _isLoading = wasLoading;
        }
    }

    private static bool HasSameBands(QcyEqualizerState? state, IReadOnlyList<QcyEqualizerBand> bands) =>
        state is not null && state.Bands.SequenceEqual(bands.Select(QcyN65Equalizer.Clamp));

    private static List<SavedEqualizerBand> ToSavedBands(IEnumerable<QcyEqualizerBand> bands) =>
        bands.Select(band => new SavedEqualizerBand
        {
            Frequency = band.Frequency,
            Gain = band.Gain,
            Q = band.Q,
            Type = band.Type,
        }).ToList();

    private static List<QcyEqualizerBand> FromSavedBands(IEnumerable<SavedEqualizerBand> bands) =>
        bands.Select(band => QcyN65Equalizer.Clamp(new QcyEqualizerBand(band.Frequency, band.Gain, band.Q, band.Type)))
            .Take(QcyN65Equalizer.MaximumBands)
            .ToList();

    private void PersistEqualizerBand()
    {
        if (!_isSynchronizingDevice && SelectedEqualizerPreset != "Custom")
        {
            SelectedEqualizerPreset = "Custom";
        }

        Save();
    }

    private void PersistAndRun(Func<QcyDeviceClient, Task> action, string successMessage)
    {
        Save();
        RunWhenConnected(action, successMessage);
    }

    private void QueueNoiseControlChange()
    {
        if (!TryGetSelectedNoiseControl(out var requested) || !CanSendToDevice)
        {
            return;
        }

        var version = Interlocked.Increment(ref _noiseControlRequestVersion);
        _pendingNoiseControl = requested;
        _noiseControlCancellation?.Cancel();
        _noiseControlCancellation?.Dispose();
        _noiseControlCancellation = new CancellationTokenSource();
        _ = ApplyNoiseControlAsync(requested, version, _noiseControlCancellation.Token);
    }

    private async Task ApplyNoiseControlAsync(
        QcyNoiseControlState requested,
        long version,
        CancellationToken cancellationToken)
    {
        var client = _deviceClient;
        if (client is null || !client.State.IsConnected)
        {
            return;
        }

        try
        {
            SaveStatus = $"Applying {NoiseControlDisplayName(requested)}…";
            await client.SetNoiseModeAsync(
                requested.Mode,
                requested.CancellationMode ?? QcyNoiseCancellationMode.Adaptive,
                cancellationToken);
            if (version != Volatile.Read(ref _noiseControlRequestVersion))
            {
                return;
            }

            if (client.State.NoiseControl != requested)
            {
                throw new InvalidOperationException("The final reading does not match the latest selection.");
            }

            _pendingNoiseControl = null;
            ApplyDeviceState(client.State);
            SaveStatus = $"{NoiseControlDisplayName(requested)} confirmed by the earbuds";
        }
        catch (OperationCanceledException) when (version != Volatile.Read(ref _noiseControlRequestVersion))
        {
        }
        catch (Exception exception)
        {
            if (version != Volatile.Read(ref _noiseControlRequestVersion))
            {
                return;
            }

            _pendingNoiseControl = null;
            ApplyDeviceState(client.State);
            SaveStatus = $"Not confirmed by the earbuds: {FriendlyError(exception)}";
        }
    }

    private async void BatteryRefreshTimer_Tick(
        Microsoft.UI.Dispatching.DispatcherQueueTimer sender,
        object args)
    {
        if (IsBusy)
        {
            return;
        }

        var client = _deviceClient;
        if (client?.State.IsConnected == true)
        {
            if (DateTimeOffset.UtcNow - _lastBatteryRefresh < TimeSpan.FromMinutes(1))
            {
                return;
            }

            try
            {
                await client.RefreshBatteryAsync();
                _lastBatteryRefresh = DateTimeOffset.UtcNow;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                BatteryStatus = "Automatic reading failed; trying again shortly";
            }

            return;
        }

        await DiscoverDevicesAsync();
    }

    private void RunWhenConnected(Func<QcyDeviceClient, Task> action, string successMessage)
    {
        if (CanSendToDevice)
        {
            _ = RunDeviceActionAsync(action, successMessage);
        }
    }

    private bool CanSendToDevice =>
        !_isLoading && !_isSynchronizingDevice && _deviceClient?.State.IsConnected == true;

    private async Task RunDeviceActionAsync(Func<QcyDeviceClient, Task> action, string successMessage)
    {
        var client = _deviceClient;
        if (client is null || !client.State.IsConnected)
        {
            return;
        }

        try
        {
            SaveStatus = "Sending to the earbuds…";
            await action(client);
            SaveStatus = successMessage;
        }
        catch (Exception exception)
        {
            SaveStatus = $"Not confirmed by the earbuds: {FriendlyError(exception)}";
        }
    }

    private async Task SendCustomEqualizerAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(300, cancellationToken);
            await RunDeviceActionAsync(
                client => client.SetN65EqualizerAsync(GetCustomEqualizerBands(), cancellationToken),
                "Custom equalizer confirmed by the earbuds");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SendChannelBalanceAfterDelayAsync(double value, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken);
            await RunDeviceActionAsync(
                client => client.SetChannelBalanceAsync((byte)Math.Clamp(Math.Round(value), 0, 100), cancellationToken),
                "Channel balance confirmed by the earbuds");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SendPromptVolumeAfterDelayAsync(double value, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken);
            await RunDeviceActionAsync(
                client => client.SetPromptVolumeAsync(value, cancellationToken),
                "Prompt volume confirmed by the earbuds");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task<BluetoothDeviceInfo?> ConnectFirstAvailableAsync(
        IEnumerable<BluetoothDeviceInfo> candidates)
    {
        foreach (var candidate in candidates.DistinctBy(device =>
                     (device.BluetoothAddress, device.ControlAddress, device.OtherAddress)))
        {
            try
            {
                ConnectionStatus = "Connecting to the QCY control service";
                _connection = await _bluetoothTransport.ConnectAsync(candidate);
                ConnectionStatus = "Reading earbud settings and battery";
                _deviceClient = await QcyDeviceClient.CreateAsync(_connection);
                _deviceClient.StateChanged += DeviceClient_StateChanged;
                return candidate;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                await DisconnectCoreAsync(updateStatus: false);
            }
        }

        return null;
    }

    private BluetoothDeviceInfo? CreateRememberedDevice(DeviceProfile profile)
    {
        var primaryAddress = profile.KnownBluetoothAddress ??
            profile.KnownControlAddress ??
            profile.KnownOtherAddress;
        return primaryAddress is null
            ? null
            : new BluetoothDeviceInfo(
                QcyAdvertisement.FormatAddress(primaryAddress.Value),
                DeviceName,
                primaryAddress.Value,
                profile.KnownControlAddress,
                profile.KnownOtherAddress,
                profile.IsN65Model ? QcyUuids.N65VendorId : QcyUuids.N70BlackVendorId,
                short.MinValue,
                0,
                0,
                0,
                false,
                false,
                false,
                DateTimeOffset.UtcNow);
    }

    private void RememberDevice(BluetoothDeviceInfo device)
    {
        if (device.BluetoothAddress != 0)
        {
            _knownBluetoothAddress = device.BluetoothAddress;
        }

        _knownControlAddress = device.ControlAddress ?? _knownControlAddress;
        _knownOtherAddress = device.OtherAddress ?? _knownOtherAddress;
    }

    private async Task DisconnectCoreAsync(bool updateStatus)
    {
        _promptVolumeDebounce?.Cancel();
        _channelBalanceDebounce?.Cancel();
        _customEqualizerDebounce?.Cancel();
        _noiseControlCancellation?.Cancel();
        _pendingNoiseControl = null;
        if (_deviceClient is not null)
        {
            _deviceClient.StateChanged -= DeviceClient_StateChanged;
            await _deviceClient.DisposeAsync();
        }
        else if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }

        _deviceClient = null;
        _connection = null;
        IsDeviceConnected = false;
        WearDetectionSupported = false;
        EqualizerSupported = false;
        EqualizerPresetsSupported = false;
        EqualizerCapabilityStatus = "Availability checked on connection";
        KeyFunctionsSupported = false;
        if (updateStatus)
        {
            ConnectionStatus = "Control disconnected";
            DiscoveryStatus = "Control connection closed";
        }
    }

    private void LoadProfile()
    {
        var profile = _profileStore.Load();
        if (profile is null)
        {
            Save();
            return;
        }

        _isLoading = true;
        try
        {
            _knownBluetoothAddress = profile.KnownBluetoothAddress;
            _knownControlAddress = profile.KnownControlAddress;
            _knownOtherAddress = profile.KnownOtherAddress;
            SetModel(profile.IsN65Model);
            SelectedNoiseMode = profile.SelectedNoiseMode;
            SelectedNoiseCancellationMode = profile.SelectedNoiseCancellationMode;
            SelectedEqualizerPreset = profile.SelectedEqualizerPreset;
            WearDetection = profile.WearDetection;
            LdacEnabled = profile.LdacEnabled;
            MultipointEnabled = profile.MultipointEnabled;
            GameModeEnabled = profile.GameModeEnabled;
            WindReductionEnabled = profile.WindReductionEnabled;
            SleepModeEnabled = profile.SleepModeEnabled;
            AutoApplyEnabled = profile.AutoApplyEnabled;
            PromptVolume = profile.PromptVolume;
            DynamicEqualizerEnabled = profile.DynamicEqualizerEnabled;
            ChannelBalance = profile.ChannelBalance;
            _savedEqualizerCurves = profile.N65EqualizerCurves;
            RefreshSavedEqualizerCurves(null);
            var customBands = FromSavedBands(profile.N65CustomBands);
            LoadCustomEqualizerBands(customBands.Count > 0 ? customBands : QcyN65Equalizer.FactoryTuning.Bands);
            foreach (var gesture in N65Gestures)
            {
                gesture.Action = profile.N65Gestures.GetValueOrDefault(gesture.Name);
            }
            EqBand1 = profile.EqBands.ElementAtOrDefault(0);
            EqBand2 = profile.EqBands.ElementAtOrDefault(1);
            EqBand3 = profile.EqBands.ElementAtOrDefault(2);
            EqBand4 = profile.EqBands.ElementAtOrDefault(3);
            EqBand5 = profile.EqBands.ElementAtOrDefault(4);
            EqBand6 = profile.EqBands.ElementAtOrDefault(5);
            EqBand7 = profile.EqBands.ElementAtOrDefault(6);
            EqBand8 = profile.EqBands.ElementAtOrDefault(7);
            EqBand9 = profile.EqBands.ElementAtOrDefault(8);
            EqBand10 = profile.EqBands.ElementAtOrDefault(9);
            LeftDoubleTap = profile.LeftDoubleTap;
            RightDoubleTap = profile.RightDoubleTap;
            LeftLongPress = profile.LeftLongPress;
            RightLongPress = profile.RightLongPress;
            DisconnectTimeout = profile.DisconnectTimeout;
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void LoadStartupSetting()
    {
        _isUpdatingStartupSetting = true;
        try
        {
            RunAtStartupEnabled = _windowsStartupService.IsEnabled;
            StartupStatus = RunAtStartupEnabled
                ? "Will start in the background, in the notification area only."
                : "Opens in the background, in the notification area only.";
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or InvalidOperationException)
        {
            RunAtStartupEnabled = false;
            StartupStatus = "Could not read the startup setting.";
        }
        finally
        {
            _isUpdatingStartupSetting = false;
        }
    }

    private void Save()
    {
        if (_isLoading || _isSynchronizingDevice)
        {
            return;
        }

        _profileStore.Save(CreateProfile());
    }

    private DeviceProfile CreateProfile() => new()
    {
        ProfileVersion = 4,
        KnownBluetoothAddress = _knownBluetoothAddress,
        KnownControlAddress = _knownControlAddress,
        KnownOtherAddress = _knownOtherAddress,
        SelectedNoiseMode = SelectedNoiseMode,
        SelectedNoiseCancellationMode = SelectedNoiseCancellationMode,
        SelectedEqualizerPreset = SelectedEqualizerPreset,
        WearDetection = WearDetection,
        LdacEnabled = LdacEnabled,
        MultipointEnabled = MultipointEnabled,
        GameModeEnabled = GameModeEnabled,
        WindReductionEnabled = WindReductionEnabled,
        SleepModeEnabled = SleepModeEnabled,
        AutoApplyEnabled = AutoApplyEnabled,
        PromptVolume = PromptVolume,
        IsN65Model = IsN65Model,
        DynamicEqualizerEnabled = DynamicEqualizerEnabled,
        ChannelBalance = ChannelBalance,
        N65CustomBands = ToSavedBands(GetCustomEqualizerBands()),
        N65EqualizerCurves = _savedEqualizerCurves,
        N65Gestures = N65Gestures
            .Where(gesture => gesture.Action is not null)
            .ToDictionary(gesture => gesture.Name, gesture => gesture.Action!),
        EqBands = GetEqualizerBands(),
        LeftDoubleTap = LeftDoubleTap,
        RightDoubleTap = RightDoubleTap,
        LeftLongPress = LeftLongPress,
        RightLongPress = RightLongPress,
        DisconnectTimeout = DisconnectTimeout,
    };

    private double[] GetEqualizerBands() =>
        [EqBand1, EqBand2, EqBand3, EqBand4, EqBand5, EqBand6, EqBand7, EqBand8, EqBand9, EqBand10];

    private void UpdateBatteryDisplays()
    {
        if (_isAggregateBatteryReading)
        {
            LeftBatteryDisplay = $"{LeftBattery:F0}%*";
            RightBatteryDisplay = $"{RightBattery:F0}%*";
            CaseBatteryDisplay = "—";
            return;
        }

        LeftBatteryDisplay = IsDeviceConnected || LeftBattery > 0 ? $"{LeftBattery:F0}%" : "—";
        RightBatteryDisplay = IsDeviceConnected || RightBattery > 0 ? $"{RightBattery:F0}%" : "—";
        CaseBatteryDisplay = IsDeviceConnected || CaseBattery > 0 ? $"{CaseBattery:F0}%" : "—";
    }

    private bool HasBatteryReading => LeftBattery > 0 || RightBattery > 0 || CaseBattery > 0;

    private static Dictionary<byte, byte> BuildKeyFunctionMap(
        DeviceProfile profile,
        IReadOnlyDictionary<byte, byte> current)
    {
        var mappings = new Dictionary<byte, byte>(current)
        {
            [0x03] = TouchActionIds[profile.LeftDoubleTap],
            [0x04] = TouchActionIds[profile.RightDoubleTap],
            [0x09] = TouchActionIds[profile.LeftLongPress],
            [0x0A] = TouchActionIds[profile.RightLongPress],
        };
        return mappings;
    }

    private static QcyNoiseMode ToNoiseMode(string value) => value switch
    {
        "Transparency" => QcyNoiseMode.Transparency,
        "Normal" => QcyNoiseMode.Normal,
        _ => QcyNoiseMode.NoiseCancellation,
    };

    private static QcyNoiseControlState ToNoiseControl(string mode, string cancellationMode, bool isN65) =>
        QcyNoiseControlState.Create(
            ToNoiseMode(mode),
            isN65
                ? QcyNoiseCancellationMode.Adaptive
                : NoiseCancellationModeIds.GetValueOrDefault(
                    cancellationMode,
                    QcyNoiseCancellationMode.Adaptive));

    private bool TryGetSelectedNoiseControl(out QcyNoiseControlState state)
    {
        if (SelectedNoiseMode is null || !NoiseModes.Contains(SelectedNoiseMode, StringComparer.Ordinal) ||
            SelectedNoiseCancellationMode is null ||
            !NoiseCancellationModeIds.ContainsKey(SelectedNoiseCancellationMode))
        {
            state = null!;
            return false;
        }

        state = ToNoiseControl(SelectedNoiseMode, SelectedNoiseCancellationMode, IsN65Model);
        return true;
    }

    private static string FromNoiseCancellationMode(QcyNoiseCancellationMode value) =>
        NoiseCancellationModeIds.First(pair => pair.Value == value).Key;

    private static string NoiseControlDisplayName(QcyNoiseControlState state) =>
        state.Mode == QcyNoiseMode.NoiseCancellation && state.CancellationMode.HasValue
            ? FromNoiseCancellationMode(state.CancellationMode.Value)
            : FromNoiseMode(state.Mode);

    private static string FromNoiseMode(QcyNoiseMode value) => value switch
    {
        QcyNoiseMode.Transparency => "Transparency",
        QcyNoiseMode.Normal => "Normal",
        _ => "Noise cancellation",
    };

    private static ushort ToAutoPowerOffMinutes(string value) => value switch
    {
        "5 minutes" => 5,
        "15 minutes" => 15,
        "30 minutes" => 30,
        "1 hour" => 60,
        _ => ushort.MaxValue,
    };

    private static string FromAutoPowerOffMinutes(ushort value) => value switch
    {
        5 => "5 minutes",
        15 => "15 minutes",
        30 => "30 minutes",
        60 => "1 hour",
        _ => "Never",
    };

    private static string FromTouchAction(byte id) =>
        TouchActionIds.FirstOrDefault(pair => pair.Value == id).Key ?? "No action";

    private static string FriendlyError(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Windows blocked Bluetooth access for the app.",
        OperationCanceledException => "Bluetooth operation canceled.",
        _ => exception.Message,
    };
}
