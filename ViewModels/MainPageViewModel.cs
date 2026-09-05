using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenQCY_Desktop.Bluetooth;
using OpenQCY_Desktop.Device;
using OpenQCY_Desktop.Protocol;
using OpenQCY_Desktop.Services;

namespace OpenQCY_Desktop.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private static readonly IReadOnlyDictionary<string, byte> EqualizerPresetIds =
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
    private CancellationTokenSource? _promptVolumeDebounce;
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
        LoadProfile();
        LoadStartupSetting();
    }

    public IReadOnlyList<string> NoiseModes { get; } = ["Noise cancellation", "Transparency", "Normal"];
    public IReadOnlyList<string> NoiseCancellationModes { get; } = [.. NoiseCancellationModeIds.Keys];
    public IReadOnlyList<string> EqualizerPresets { get; } = [.. EqualizerPresetIds.Keys, "Custom"];
    public IReadOnlyList<string> TouchActions { get; } = [.. TouchActionIds.Keys];
    public IReadOnlyList<string> DisconnectTimeouts { get; } = ["Never", "5 minutes", "15 minutes", "30 minutes", "1 hour"];

    [ObservableProperty]
    public partial string SelectedNoiseMode { get; set; } = "Noise cancellation";

    [ObservableProperty]
    public partial string SelectedNoiseCancellationMode { get; set; } = "Adaptive";

    public bool IsNoiseCancellationSelected => SelectedNoiseMode == "Noise cancellation";

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
    public partial string DeviceName { get; set; } = "QCY MeloBuds N70";

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
    public partial string BatteryStatus { get; set; } = "Looking for a recent N70 battery reading";

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

            DiscoveryStatus = "Looking for the N70 already known to Windows…";
            ConnectionStatus = "Looking for the saved control channel";

            var knownDevices = (await _bluetoothTransport.FindPairedQcyDevicesAsync())
                .Where(device => QcyUuids.IsN70(device.VendorId))
                .ToList();
            var rememberedDevice = CreateRememberedDevice(desiredProfile);
            if (rememberedDevice is not null)
            {
                knownDevices.Insert(0, rememberedDevice);
            }

            var target = await ConnectFirstAvailableAsync(knownDevices);
            if (target is null)
            {
                DiscoveryStatus = "The N70 was not cached; scanning for BLE advertisements…";
                ConnectionStatus = "Looking for the control channel advertisement";
                var advertisedDevices = await _bluetoothTransport.ScanForQcyDevicesAsync(TimeSpan.FromSeconds(8));
                var advertisedN70 = advertisedDevices
                    .Where(device => QcyUuids.IsN70(device.VendorId))
                    .ToArray();
                target = await ConnectFirstAvailableAsync(advertisedN70);
                if (target is null)
                {
                    throw new InvalidOperationException(
                        "The N70 was not found in the Windows cache and did not advertise its control channel. Open the case or remove an earbud and try again.");
                }
            }

            DeviceName = target.Name;
            RememberDevice(target);
            if (target.SignalStrength != short.MinValue)
            {
                _isAggregateBatteryReading = false;
                LeftBattery = target.LeftBattery;
                RightBattery = target.RightBattery;
                CaseBattery = target.CaseBattery;
                UpdateBatteryDisplays();
                BatteryStatus = "Battery detected from the N70 advertisement";
            }

            ApplyDeviceState(_deviceClient!.State);

            if (desiredProfile.AutoApplyEnabled)
            {
                SaveStatus = "Reapplying the saved profile to the N70…";
                await ApplyProfileToDeviceAsync(desiredProfile);
            }

            ApplyDeviceState(_deviceClient.State);
            DiscoveryStatus = "QCY control channel connected and validated";
            BatteryStatus = "Updates automatically while the control channel is connected";
            Save();
            SaveStatus = desiredProfile.AutoApplyEnabled
                ? "Profile confirmed by the N70"
                : "Current N70 state loaded";
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
                ? "Showing the last reading received from the N70"
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
            SaveStatus = "Profile saved — connect the N70 to apply it";
            return;
        }

        await RunDeviceActionAsync(
            client => ApplyProfileToDeviceAsync(CreateProfile()),
            "Profile applied and confirmed by the N70");
    }

    [RelayCommand]
    private async Task ApplyEqualizerAsync()
    {
        if (_deviceClient is null || !_deviceClient.State.IsConnected)
        {
            SaveStatus = "Equalizer saved — connect the N70 to apply it";
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
                "Custom equalizer confirmed by the N70");
            return;
        }

        if (!EqualizerPresetsSupported)
        {
            SaveStatus = "Firmware 3.0.13 supports the custom curve but does not expose the legacy preset channel";
            return;
        }

        await RunDeviceActionAsync(
            client => client.SetEqualizerPresetAsync(EqualizerPresetIds[SelectedEqualizerPreset]),
            "Preset confirmed by the N70");
    }

    [RelayCommand]
    private void SaveProfile()
    {
        Save();
        SaveStatus = "Preferences saved on this PC";
    }

    [RelayCommand]
    private void ResetEqualizer()
    {
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
            EqualizerPresetIds.TryGetValue(value, out var preset))
        {
            RunWhenConnected(client => client.SetEqualizerPresetAsync(preset), "Equalizer confirmed by the N70");
        }
    }

    partial void OnWearDetectionChanged(bool value) => PersistAndRun(
        client => client.SetWearDetectionAsync(value),
        value ? "Wear detection enabled on the N70" : "Wear detection disabled on the N70");

    partial void OnLdacEnabledChanged(bool value) => PersistAndRun(
        client => client.SetLdacAsync(value),
        "LDAC sent to the N70; the earbuds may restart the connection");

    partial void OnMultipointEnabledChanged(bool value) => PersistAndRun(
        client => client.SetMultipointAsync(value),
        "Multipoint sent to the N70; the earbuds may restart the connection");

    partial void OnGameModeEnabledChanged(bool value) => PersistAndRun(
        client => client.SetGameModeAsync(value),
        "Game mode confirmed by the N70");

    partial void OnWindReductionEnabledChanged(bool value) => PersistAndRun(
        client => client.SetWindDetectionAsync(value),
        "Wind detection confirmed by the N70");

    partial void OnSleepModeEnabledChanged(bool value) => PersistAndRun(
        client => client.SetSleepModeAsync(value),
        "Sleep mode confirmed by the N70");

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
        "Automatic power off confirmed by the N70");

    private async Task ApplyProfileToDeviceAsync(DeviceProfile profile)
    {
        var client = _deviceClient ?? throw new InvalidOperationException("The N70 is not connected.");
        var state = client.State;

        if (state.WearDetectionEnabled.HasValue && state.WearDetectionEnabled != profile.WearDetection)
        {
            await client.SetWearDetectionAsync(profile.WearDetection);
        }

        var noiseControl = ToNoiseControl(profile.SelectedNoiseMode, profile.SelectedNoiseCancellationMode);
        if (state.NoiseControl is not null && state.NoiseControl != noiseControl)
        {
            await client.SetNoiseModeAsync(
                noiseControl.Mode,
                noiseControl.CancellationMode ?? QcyNoiseCancellationMode.Adaptive);
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
            EqualizerPresetIds.TryGetValue(profile.SelectedEqualizerPreset, out var preset) &&
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

    private void DeviceClient_StateChanged(object? sender, QcyDeviceState state)
    {
        if (App.DispatcherQueue.HasThreadAccess)
        {
            ApplyDeviceState(state);
        }
        else
        {
            App.DispatcherQueue.TryEnqueue(() => ApplyDeviceState(state));
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

            if (state.EqualizerPreset.HasValue)
            {
                SelectedEqualizerPreset = EqualizerPresetIds
                    .FirstOrDefault(pair => pair.Value == state.EqualizerPreset.Value).Key ?? SelectedEqualizerPreset;
            }

            if (state.EqualizerGains.Count == 10)
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

    private void UpdateCapabilities(QcyDeviceState state)
    {
        var characteristics = _connection?.Characteristics ?? [];
        var commandChannelSupported = state.IsConnected &&
            characteristics.Any(info => info.Uuid == QcyUuids.Command && info.CanWrite) &&
            characteristics.Any(info => info.Uuid == QcyUuids.Notification && info.CanNotify);
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

    private void PersistTouchFunctions()
    {
        Save();
        RunWhenConnected(
            client => client.SetKeyFunctionsAsync(BuildKeyFunctionMap(CreateProfile(), client.State.KeyFunctions)),
            "Gestures confirmed by the N70");
    }

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
            SaveStatus = $"{NoiseControlDisplayName(requested)} confirmed by the N70";
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
            SaveStatus = $"Not confirmed by the N70: {FriendlyError(exception)}";
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
            SaveStatus = "Sending to the N70…";
            await action(client);
            SaveStatus = successMessage;
        }
        catch (Exception exception)
        {
            SaveStatus = $"Not confirmed by the N70: {FriendlyError(exception)}";
        }
    }

    private async Task SendPromptVolumeAfterDelayAsync(double value, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken);
            await RunDeviceActionAsync(
                client => client.SetPromptVolumeAsync(value, cancellationToken),
                "Prompt volume confirmed by the N70");
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
                ConnectionStatus = "Connecting to the QCY A001 service";
                _connection = await _bluetoothTransport.ConnectAsync(candidate);
                ConnectionStatus = "Reading N70 settings and battery";
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
                QcyUuids.N70BlackVendorId,
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

    private static QcyNoiseControlState ToNoiseControl(string mode, string cancellationMode) =>
        QcyNoiseControlState.Create(
            ToNoiseMode(mode),
            NoiseCancellationModeIds.GetValueOrDefault(
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

        state = ToNoiseControl(SelectedNoiseMode, SelectedNoiseCancellationMode);
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
