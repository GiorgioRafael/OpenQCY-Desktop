using OpenQCY_Desktop.Protocol;

namespace OpenQCY_Desktop.Device;

public enum QcyWearDetectionProtocol
{
    Unknown,
    Legacy,
    WearingDetection,
}

public sealed record QcyBatteryState(
    byte? Left,
    byte? Right,
    byte? Case,
    bool LeftCharging = false,
    bool RightCharging = false,
    bool CaseCharging = false);

public sealed record QcyDeviceState
{
    public bool IsConnected { get; init; }
    public string DeviceName { get; init; } = "QCY device";
    public QcyBatteryState Battery { get; init; } = new(null, null, null);
    public string? FirmwareVersion { get; init; }
    public bool? WearDetectionEnabled { get; init; }
    public QcyWearDetectionProtocol WearDetectionProtocol { get; init; }
    public QcyWearingDetection? WearingDetection { get; init; }
    public QcyNoiseMode? NoiseMode { get; init; }
    public bool? GameModeEnabled { get; init; }
    public bool? SleepModeEnabled { get; init; }
    public bool? LdacEnabled { get; init; }
    public bool? MultipointEnabled { get; init; }
    public bool? WindDetectionEnabled { get; init; }
    public byte? PromptVolume { get; init; }
    public byte PromptVolumeMaximum { get; init; } = 100;
    public ushort? AutoPowerOffMinutes { get; init; }
    public IReadOnlyDictionary<byte, byte> KeyFunctions { get; init; } = new Dictionary<byte, byte>();
    public byte? EqualizerPreset { get; init; }
}
