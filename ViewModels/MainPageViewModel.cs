using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenQCY_Desktop.Bluetooth;
using OpenQCY_Desktop.Services;

namespace OpenQCY_Desktop.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly ProfileStore _profileStore = new();
    private readonly IBluetoothTransport _bluetoothTransport = new WindowsBluetoothTransport();
    private bool _isLoading;

    public MainPageViewModel()
    {
        LoadProfile();
    }

    public IReadOnlyList<string> NoiseModes { get; } = ["Cancelamento", "Transparência", "Normal"];
    public IReadOnlyList<string> EqualizerPresets { get; } = ["Balanceado", "Graves fortes", "Vozes", "Detalhado", "Personalizado"];
    public IReadOnlyList<string> TouchActions { get; } = ["Reproduzir / pausar", "Próxima faixa", "Faixa anterior", "Modo de ruído", "Assistente", "Modo jogo", "Sem ação"];
    public IReadOnlyList<string> DisconnectTimeouts { get; } = ["Nunca", "5 minutos", "15 minutos", "30 minutos", "1 hora"];

    [ObservableProperty]
    public partial string SelectedNoiseMode { get; set; } = "Cancelamento";

    [ObservableProperty]
    public partial string SelectedEqualizerPreset { get; set; } = "Balanceado";

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
    public partial double EqBand1 { get; set; } = 50;

    [ObservableProperty]
    public partial double EqBand2 { get; set; } = 54;

    [ObservableProperty]
    public partial double EqBand3 { get; set; } = 52;

    [ObservableProperty]
    public partial double EqBand4 { get; set; } = 50;

    [ObservableProperty]
    public partial double EqBand5 { get; set; } = 48;

    [ObservableProperty]
    public partial double EqBand6 { get; set; } = 50;

    [ObservableProperty]
    public partial double EqBand7 { get; set; } = 53;

    [ObservableProperty]
    public partial double EqBand8 { get; set; } = 55;

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
    public partial string DiscoveryStatus { get; set; } = "Descoberta BLE somente leitura pronta";

    [RelayCommand]
    private async Task DiscoverDevicesAsync()
    {
        DiscoveryStatus = "Procurando dispositivos QCY pareados…";

        try
        {
            var devices = await _bluetoothTransport.FindPairedQcyDevicesAsync();
            DiscoveryStatus = devices.Count switch
            {
                0 => "Áudio pareado; canal de configuração BLE ainda não apareceu no Windows",
                1 => $"Canal BLE encontrado: {devices[0].Name}",
                _ => $"{devices.Count} dispositivos QCY encontrados",
            };
        }
        catch (UnauthorizedAccessException)
        {
            DiscoveryStatus = "O Windows bloqueou o acesso Bluetooth para este aplicativo";
        }
        catch (Exception)
        {
            DiscoveryStatus = "Não foi possível consultar o Bluetooth agora";
        }
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
        EqBand1 = 50;
        EqBand2 = 50;
        EqBand3 = 50;
        EqBand4 = 50;
        EqBand5 = 50;
        EqBand6 = 50;
        EqBand7 = 50;
        EqBand8 = 50;
        SelectedEqualizerPreset = "Balanceado";
    }

    [RelayCommand]
    private void MarkProtocolPending()
    {
        SaveStatus = "Salvo localmente — envio ao N70 aguarda o mapeamento do protocolo";
        Save();
    }

    partial void OnSelectedNoiseModeChanged(string value) => Save();
    partial void OnSelectedEqualizerPresetChanged(string value) => Save();
    partial void OnWearDetectionChanged(bool value) => Save();
    partial void OnLdacEnabledChanged(bool value) => Save();
    partial void OnMultipointEnabledChanged(bool value) => Save();
    partial void OnGameModeEnabledChanged(bool value) => Save();
    partial void OnWindReductionEnabledChanged(bool value) => Save();
    partial void OnSleepModeEnabledChanged(bool value) => Save();
    partial void OnAutoApplyEnabledChanged(bool value) => Save();
    partial void OnPromptVolumeChanged(double value)
    {
        OnPropertyChanged(nameof(PromptVolumeDisplay));
        Save();
    }
    partial void OnEqBand1Changed(double value) => Save();
    partial void OnEqBand2Changed(double value) => Save();
    partial void OnEqBand3Changed(double value) => Save();
    partial void OnEqBand4Changed(double value) => Save();
    partial void OnEqBand5Changed(double value) => Save();
    partial void OnEqBand6Changed(double value) => Save();
    partial void OnEqBand7Changed(double value) => Save();
    partial void OnEqBand8Changed(double value) => Save();
    partial void OnLeftDoubleTapChanged(string value) => Save();
    partial void OnRightDoubleTapChanged(string value) => Save();
    partial void OnLeftLongPressChanged(string value) => Save();
    partial void OnRightLongPressChanged(string value) => Save();
    partial void OnDisconnectTimeoutChanged(string value) => Save();

    private void LoadProfile()
    {
        var profile = _profileStore.Load();
        if (profile is null)
        {
            Save();
            return;
        }

        _isLoading = true;
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
        LeftDoubleTap = profile.LeftDoubleTap;
        RightDoubleTap = profile.RightDoubleTap;
        LeftLongPress = profile.LeftLongPress;
        RightLongPress = profile.RightLongPress;
        DisconnectTimeout = profile.DisconnectTimeout;
        _isLoading = false;
    }

    private void Save()
    {
        if (_isLoading)
        {
            return;
        }

        _profileStore.Save(new DeviceProfile
        {
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
            EqBands = [EqBand1, EqBand2, EqBand3, EqBand4, EqBand5, EqBand6, EqBand7, EqBand8],
            LeftDoubleTap = LeftDoubleTap,
            RightDoubleTap = RightDoubleTap,
            LeftLongPress = LeftLongPress,
            RightLongPress = RightLongPress,
            DisconnectTimeout = DisconnectTimeout,
        });
    }
}
