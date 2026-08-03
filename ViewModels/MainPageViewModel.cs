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
            ["Som espacial"] = 0,
            ["Padrão"] = 1,
            ["Palco suave"] = 2,
            ["Pop"] = 3,
            ["Graves fortes"] = 4,
            ["Rock"] = 5,
            ["Suave"] = 6,
            ["Clássico"] = 7,
        };

    private static readonly IReadOnlyDictionary<string, byte> TouchActionIds =
        new Dictionary<string, byte>(StringComparer.Ordinal)
        {
            ["Sem ação"] = 0,
            ["Reproduzir / pausar"] = 1,
            ["Faixa anterior"] = 2,
            ["Próxima faixa"] = 3,
            ["Assistente"] = 4,
            ["Aumentar volume"] = 5,
            ["Diminuir volume"] = 6,
            ["Modo jogo"] = 7,
            ["Modo de ruído"] = 8,
        };

    private readonly ProfileStore _profileStore = new();
    private readonly IBluetoothTransport _bluetoothTransport = new WindowsBluetoothTransport();
    private IBluetoothDeviceConnection? _connection;
    private QcyDeviceClient? _deviceClient;
    private CancellationTokenSource? _promptVolumeDebounce;
    private bool _isLoading;
    private bool _isSynchronizingDevice;

    public MainPageViewModel()
    {
        LoadProfile();
    }

    public IReadOnlyList<string> NoiseModes { get; } = ["Cancelamento", "Transparência", "Normal"];
    public IReadOnlyList<string> EqualizerPresets { get; } = [.. EqualizerPresetIds.Keys, "Personalizado"];
    public IReadOnlyList<string> TouchActions { get; } = [.. TouchActionIds.Keys];
    public IReadOnlyList<string> DisconnectTimeouts { get; } = ["Nunca", "5 minutos", "15 minutos", "30 minutos", "1 hora"];

    [ObservableProperty]
    public partial string SelectedNoiseMode { get; set; } = "Cancelamento";

    [ObservableProperty]
    public partial string SelectedEqualizerPreset { get; set; } = "Padrão";

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
    public partial string LeftDoubleTap { get; set; } = "Reproduzir / pausar";

    [ObservableProperty]
    public partial string RightDoubleTap { get; set; } = "Próxima faixa";

    [ObservableProperty]
    public partial string LeftLongPress { get; set; } = "Modo de ruído";

    [ObservableProperty]
    public partial string RightLongPress { get; set; } = "Assistente";

    [ObservableProperty]
    public partial string DisconnectTimeout { get; set; } = "Nunca";

    [ObservableProperty]
    public partial string SaveStatus { get; set; } = "Preferências locais carregadas";

    [ObservableProperty]
    public partial string DiscoveryStatus { get; set; } = "Pronto para procurar o canal de controle QCY";

    [ObservableProperty]
    public partial string DeviceName { get; set; } = "QCY MeloBuds N70";

    [ObservableProperty]
    public partial string ConnectionStatus { get; set; } = "Controle desconectado";

    [ObservableProperty]
    public partial string FirmwareVersion { get; set; } = "—";

    [ObservableProperty]
    public partial string LeftBatteryDisplay { get; set; } = "—";

    [ObservableProperty]
    public partial string RightBatteryDisplay { get; set; } = "—";

    [ObservableProperty]
    public partial string CaseBatteryDisplay { get; set; } = "—";

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
    public partial bool KeyFunctionsSupported { get; set; }

    [RelayCommand]
    private async Task DiscoverDevicesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        var desiredProfile = CreateProfile();
        try
        {
            await DisconnectCoreAsync(updateStatus: false);
            DiscoveryStatus = "Procurando anúncios BLE do N70…";
            ConnectionStatus = "Procurando canal de controle";

            var devices = await _bluetoothTransport.ScanForQcyDevicesAsync(TimeSpan.FromSeconds(8));
            var target = devices.FirstOrDefault(device => QcyUuids.IsN70(device.VendorId));
            if (target is null)
            {
                throw new InvalidOperationException(
                    "O N70 não anunciou o canal de controle. Abra o estojo, retire os fones e tente novamente.");
            }

            DeviceName = target.Name;
            LeftBattery = target.LeftBattery;
            RightBattery = target.RightBattery;
            CaseBattery = target.CaseBattery;
            UpdateBatteryDisplays();
            ConnectionStatus = "Conectando ao serviço QCY A001";
            DiscoveryStatus = $"N70 encontrado com sinal de {target.SignalStrength} dBm";

            _connection = await _bluetoothTransport.ConnectAsync(target);
            ConnectionStatus = "Lendo configurações do N70";
            _deviceClient = await QcyDeviceClient.CreateAsync(_connection);
            _deviceClient.StateChanged += DeviceClient_StateChanged;
            ApplyDeviceState(_deviceClient.State);
            UpdateCapabilities();

            if (desiredProfile.AutoApplyEnabled)
            {
                SaveStatus = "Reaplicando o perfil salvo no N70…";
                await ApplyProfileToDeviceAsync(desiredProfile);
            }

            ApplyDeviceState(_deviceClient.State);
            DiscoveryStatus = "Canal de controle QCY conectado e validado";
            SaveStatus = desiredProfile.AutoApplyEnabled
                ? "Perfil confirmado pelo N70"
                : "Estado atual lido do N70";
        }
        catch (Exception exception)
        {
            await DisconnectCoreAsync(updateStatus: false);
            ConnectionStatus = "Controle desconectado";
            DiscoveryStatus = FriendlyError(exception);
            SaveStatus = "As preferências continuam salvas neste PC";
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
            SaveStatus = "Perfil salvo — conecte o N70 para aplicá-lo";
            return;
        }

        await RunDeviceActionAsync(
            client => ApplyProfileToDeviceAsync(CreateProfile()),
            "Perfil aplicado e confirmado pelo N70");
    }

    [RelayCommand]
    private async Task ApplyEqualizerAsync()
    {
        if (_deviceClient is null || !_deviceClient.State.IsConnected)
        {
            SaveStatus = "Equalizador salvo — conecte o N70 para aplicá-lo";
            return;
        }

        await RunDeviceActionAsync(
            client => SelectedEqualizerPreset == "Personalizado"
                ? client.SetCustomEqualizerAsync(GetEqualizerBands())
                : client.SetEqualizerPresetAsync(EqualizerPresetIds[SelectedEqualizerPreset]),
            "Equalizador confirmado pelo N70");
    }

    [RelayCommand]
    private void SaveProfile()
    {
        Save();
        SaveStatus = "Preferências salvas neste PC";
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
            SelectedEqualizerPreset = "Personalizado";
        }
        finally
        {
            _isSynchronizingDevice = false;
        }

        Save();
        SaveStatus = "Equalizador zerado — clique em Aplicar";
    }

    public void Shutdown() => _ = DisconnectCoreAsync(updateStatus: false);

    partial void OnSelectedNoiseModeChanged(string value) => PersistAndRun(
        client => client.SetNoiseModeAsync(ToNoiseMode(value)),
        "Controle de ruído confirmado pelo N70");

    partial void OnSelectedEqualizerPresetChanged(string value)
    {
        Save();
        if (value != "Personalizado" && EqualizerPresetIds.TryGetValue(value, out var preset))
        {
            RunWhenConnected(client => client.SetEqualizerPresetAsync(preset), "Equalizador confirmado pelo N70");
        }
    }

    partial void OnWearDetectionChanged(bool value) => PersistAndRun(
        client => client.SetWearDetectionAsync(value),
        value ? "Detecção de uso ligada no N70" : "Detecção de uso desligada no N70");

    partial void OnLdacEnabledChanged(bool value) => PersistAndRun(
        client => client.SetLdacAsync(value),
        "LDAC enviado ao N70; o fone pode reiniciar a conexão");

    partial void OnMultipointEnabledChanged(bool value) => PersistAndRun(
        client => client.SetMultipointAsync(value),
        "Multiponto enviado ao N70; o fone pode reiniciar a conexão");

    partial void OnGameModeEnabledChanged(bool value) => PersistAndRun(
        client => client.SetGameModeAsync(value),
        "Modo jogo confirmado pelo N70");

    partial void OnWindReductionEnabledChanged(bool value) => PersistAndRun(
        client => client.SetWindDetectionAsync(value),
        "Detecção de vento confirmada pelo N70");

    partial void OnSleepModeEnabledChanged(bool value) => PersistAndRun(
        client => client.SetSleepModeAsync(value),
        "Modo sono confirmado pelo N70");

    partial void OnAutoApplyEnabledChanged(bool value) => Save();

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

    partial void OnEqBand1Changed(double value) => Save();
    partial void OnEqBand2Changed(double value) => Save();
    partial void OnEqBand3Changed(double value) => Save();
    partial void OnEqBand4Changed(double value) => Save();
    partial void OnEqBand5Changed(double value) => Save();
    partial void OnEqBand6Changed(double value) => Save();
    partial void OnEqBand7Changed(double value) => Save();
    partial void OnEqBand8Changed(double value) => Save();
    partial void OnEqBand9Changed(double value) => Save();
    partial void OnEqBand10Changed(double value) => Save();

    partial void OnLeftDoubleTapChanged(string value) => PersistTouchFunctions();
    partial void OnRightDoubleTapChanged(string value) => PersistTouchFunctions();
    partial void OnLeftLongPressChanged(string value) => PersistTouchFunctions();
    partial void OnRightLongPressChanged(string value) => PersistTouchFunctions();

    partial void OnDisconnectTimeoutChanged(string value) => PersistAndRun(
        client => client.SetAutoPowerOffAsync(ToAutoPowerOffMinutes(value)),
        "Desligamento automático confirmado pelo N70");

    private async Task ApplyProfileToDeviceAsync(DeviceProfile profile)
    {
        var client = _deviceClient ?? throw new InvalidOperationException("O N70 não está conectado.");
        var state = client.State;

        if (state.WearDetectionEnabled.HasValue && state.WearDetectionEnabled != profile.WearDetection)
        {
            await client.SetWearDetectionAsync(profile.WearDetection);
        }

        var noiseMode = ToNoiseMode(profile.SelectedNoiseMode);
        if (state.NoiseMode.HasValue && state.NoiseMode != noiseMode)
        {
            await client.SetNoiseModeAsync(noiseMode);
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

        if (EqualizerSupported)
        {
            if (profile.SelectedEqualizerPreset == "Personalizado")
            {
                await client.SetCustomEqualizerAsync(profile.EqBands);
            }
            else if (EqualizerPresetIds.TryGetValue(profile.SelectedEqualizerPreset, out var preset) &&
                state.EqualizerPreset != preset)
            {
                await client.SetEqualizerPresetAsync(preset);
            }
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
            ConnectionStatus = state.IsConnected ? "Controle QCY conectado" : "Controle desconectado";
            DeviceName = state.DeviceName;
            FirmwareVersion = state.FirmwareVersion ?? "—";
            LeftBattery = state.Battery.Left ?? LeftBattery;
            RightBattery = state.Battery.Right ?? RightBattery;
            CaseBattery = state.Battery.Case ?? CaseBattery;
            UpdateBatteryDisplays();
            WearDetectionSupported = state.WearDetectionProtocol != QcyWearDetectionProtocol.Unknown;

            if (state.WearDetectionEnabled.HasValue)
            {
                WearDetection = state.WearDetectionEnabled.Value;
            }

            if (state.NoiseMode.HasValue)
            {
                SelectedNoiseMode = FromNoiseMode(state.NoiseMode.Value);
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
    }

    private void UpdateCapabilities()
    {
        var characteristics = _connection?.Characteristics ?? [];
        EqualizerSupported = characteristics.Any(info => info.Uuid == QcyUuids.Equalizer && info.CanWrite);
        KeyFunctionsSupported = characteristics.Any(info => info.Uuid == QcyUuids.KeyFunctions && info.CanWrite);
        WearDetectionSupported = _deviceClient?.State.WearDetectionProtocol != QcyWearDetectionProtocol.Unknown;
    }

    private void PersistTouchFunctions()
    {
        Save();
        RunWhenConnected(
            client => client.SetKeyFunctionsAsync(BuildKeyFunctionMap(CreateProfile(), client.State.KeyFunctions)),
            "Gestos confirmados pelo N70");
    }

    private void PersistAndRun(Func<QcyDeviceClient, Task> action, string successMessage)
    {
        Save();
        RunWhenConnected(action, successMessage);
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
            SaveStatus = "Enviando ao N70…";
            await action(client);
            SaveStatus = successMessage;
        }
        catch (Exception exception)
        {
            SaveStatus = $"Não confirmado pelo N70: {FriendlyError(exception)}";
        }
    }

    private async Task SendPromptVolumeAfterDelayAsync(double value, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(350, cancellationToken);
            await RunDeviceActionAsync(
                client => client.SetPromptVolumeAsync(value, cancellationToken),
                "Volume dos avisos confirmado pelo N70");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DisconnectCoreAsync(bool updateStatus)
    {
        _promptVolumeDebounce?.Cancel();
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
        KeyFunctionsSupported = false;
        if (updateStatus)
        {
            ConnectionStatus = "Controle desconectado";
            DiscoveryStatus = "Conexão de controle encerrada";
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
            SelectedNoiseMode = profile.SelectedNoiseMode;
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
        ProfileVersion = 2,
        SelectedNoiseMode = SelectedNoiseMode,
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
        LeftBatteryDisplay = IsDeviceConnected || LeftBattery > 0 ? $"{LeftBattery:F0}%" : "—";
        RightBatteryDisplay = IsDeviceConnected || RightBattery > 0 ? $"{RightBattery:F0}%" : "—";
        CaseBatteryDisplay = IsDeviceConnected || CaseBattery > 0 ? $"{CaseBattery:F0}%" : "—";
    }

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
        "Transparência" => QcyNoiseMode.Transparency,
        "Normal" => QcyNoiseMode.Normal,
        _ => QcyNoiseMode.NoiseCancellation,
    };

    private static string FromNoiseMode(QcyNoiseMode value) => value switch
    {
        QcyNoiseMode.Transparency => "Transparência",
        QcyNoiseMode.Normal => "Normal",
        _ => "Cancelamento",
    };

    private static ushort ToAutoPowerOffMinutes(string value) => value switch
    {
        "5 minutos" => 5,
        "15 minutos" => 15,
        "30 minutos" => 30,
        "1 hora" => 60,
        _ => ushort.MaxValue,
    };

    private static string FromAutoPowerOffMinutes(ushort value) => value switch
    {
        5 => "5 minutos",
        15 => "15 minutos",
        30 => "30 minutos",
        60 => "1 hora",
        _ => "Nunca",
    };

    private static string FromTouchAction(byte id) =>
        TouchActionIds.FirstOrDefault(pair => pair.Value == id).Key ?? "Sem ação";

    private static string FriendlyError(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "O Windows bloqueou o acesso Bluetooth ao aplicativo.",
        OperationCanceledException => "Operação Bluetooth cancelada.",
        _ => exception.Message,
    };
}
