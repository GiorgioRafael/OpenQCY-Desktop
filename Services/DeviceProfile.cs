namespace OpenQCY_Desktop.Services;

public sealed class DeviceProfile
{
    public int ProfileVersion { get; set; } = 4;
    public ulong? KnownBluetoothAddress { get; set; }
    public ulong? KnownControlAddress { get; set; }
    public ulong? KnownOtherAddress { get; set; }
    public string SelectedNoiseMode { get; set; } = "Noise cancellation";
    public string SelectedNoiseCancellationMode { get; set; } = "Adaptive";
    public string SelectedEqualizerPreset { get; set; } = "Default";
    public bool WearDetection { get; set; }
    public bool LdacEnabled { get; set; } = true;
    public bool MultipointEnabled { get; set; } = true;
    public bool GameModeEnabled { get; set; }
    public bool WindReductionEnabled { get; set; } = true;
    public bool SleepModeEnabled { get; set; }
    public bool AutoApplyEnabled { get; set; } = true;
    public double PromptVolume { get; set; } = 64;
    public double[] EqBands { get; set; } = new double[10];
    public string LeftDoubleTap { get; set; } = "Play / pause";
    public string RightDoubleTap { get; set; } = "Next track";
    public string LeftLongPress { get; set; } = "Noise control";
    public string RightLongPress { get; set; } = "Voice assistant";
    public string DisconnectTimeout { get; set; } = "Never";
}
