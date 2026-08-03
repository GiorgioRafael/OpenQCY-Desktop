namespace OpenQCY_Desktop.Services;

public sealed class DeviceProfile
{
    public int ProfileVersion { get; set; } = 2;
    public string SelectedNoiseMode { get; set; } = "Cancelamento";
    public string SelectedEqualizerPreset { get; set; } = "Padrão";
    public bool WearDetection { get; set; }
    public bool LdacEnabled { get; set; } = true;
    public bool MultipointEnabled { get; set; } = true;
    public bool GameModeEnabled { get; set; }
    public bool WindReductionEnabled { get; set; } = true;
    public bool SleepModeEnabled { get; set; }
    public bool AutoApplyEnabled { get; set; } = true;
    public double PromptVolume { get; set; } = 64;
    public double[] EqBands { get; set; } = new double[10];
    public string LeftDoubleTap { get; set; } = "Reproduzir / pausar";
    public string RightDoubleTap { get; set; } = "Próxima faixa";
    public string LeftLongPress { get; set; } = "Modo de ruído";
    public string RightLongPress { get; set; } = "Assistente";
    public string DisconnectTimeout { get; set; } = "Nunca";
}
