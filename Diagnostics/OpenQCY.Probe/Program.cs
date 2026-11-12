using System.Text;
using OpenQCY_Desktop.Bluetooth;
using OpenQCY_Desktop.Device;
using OpenQCY_Desktop.Protocol;

Console.OutputEncoding = Encoding.UTF8;
var willDisableWearDetection = args.Contains("--disable-wear-detection", StringComparer.OrdinalIgnoreCase);
var willTestN65Writes = args.Contains("--n65-write-test", StringComparer.OrdinalIgnoreCase);
var windowsBatteryOnly = args.Contains("--windows-battery", StringComparer.OrdinalIgnoreCase);
Console.WriteLine(willDisableWearDetection
    ? "OpenQCY Probe · diagnostics and requested wear-detection change"
    : willTestN65Writes
    ? "OpenQCY Probe · diagnostics and reversible N65 write test"
    : "OpenQCY Probe · read-only local diagnostics");
Console.WriteLine("Open the case and keep both earbuds near the computer.");

var transport = new WindowsBluetoothTransport();
if (windowsBatteryOnly)
{
    var windowsBattery = await transport.FindWindowsBatteryAsync();
    if (windowsBattery is null)
    {
        Console.Error.WriteLine("Windows did not provide a battery reading for the N70.");
        return 3;
    }

    Console.WriteLine(
        $"Windows cache: {windowsBattery.DeviceName} · {windowsBattery.Percentage}% · " +
        $"connected: {YesNo(windowsBattery.IsConnected)}");
    return 0;
}

var devices = await transport.ScanForQcyDevicesAsync(TimeSpan.FromSeconds(12));
if (devices.Count == 0)
{
    Console.Error.WriteLine("No QCY BLE advertisement (0x521C) was found.");
    return 2;
}

foreach (var device in devices)
{
    Console.WriteLine(
        $"Found: {device.Name} · vendor {device.VendorId} · RSSI {device.SignalStrength} dBm · " +
        $"L {device.LeftBattery}% / R {device.RightBattery}% / case {device.CaseBattery}%");
}

var target = devices.FirstOrDefault(device => QcyUuids.IsSupported(device.VendorId)) ?? devices[0];
Console.WriteLine($"Connecting to the {target.Name} control channel…");

await using var connection = await transport.ConnectAsync(target);
await using var client = await QcyDeviceClient.CreateAsync(connection);
client.ProtocolTrace += (_, line) => Console.WriteLine($"  {line}");
await client.RefreshAsync();

var state = client.State;
Console.WriteLine($"Connected: {state.DeviceName}");
Console.WriteLine($"Firmware: {state.FirmwareVersion ?? "not reported"}");
Console.WriteLine($"Battery: L {Percent(state.Battery.Left)} · R {Percent(state.Battery.Right)} · case {Percent(state.Battery.Case)}");
Console.WriteLine($"Wear detection: {BooleanText(state.WearDetectionEnabled)} · protocol {state.WearDetectionProtocol}");
Console.WriteLine($"ANC: {state.NoiseMode?.ToString() ?? "not reported"}");
Console.WriteLine($"Game mode: {BooleanText(state.GameModeEnabled)}");
Console.WriteLine($"LDAC: {BooleanText(state.LdacEnabled)} · multipoint: {BooleanText(state.MultipointEnabled)}");
Console.WriteLine(
    $"Protocol: {state.ProtocolVariant} · dynamic EQ: {BooleanText(state.DynamicEqualizerEnabled)} · " +
    $"balance: {state.ChannelBalance?.ToString() ?? "not reported"}");
Console.WriteLine($"Equalizer: preset {state.EqualizerPreset?.ToString() ?? "not reported"} · {state.EqualizerGains.Count} bands");
Console.WriteLine($"QCY characteristics found: {connection.Characteristics.Count}");
foreach (var characteristic in connection.Characteristics.OrderBy(item => item.Uuid))
{
    Console.WriteLine(
        $"  {characteristic.Uuid:D} · read {YesNo(characteristic.CanRead)} · " +
        $"write {YesNo(characteristic.CanWrite)} · notify {YesNo(characteristic.CanNotify)}");
}

if (willTestN65Writes && client.IsJieli)
{
    // Every change is reverted and confirmed by reading the earbuds back.
    var original = client.State;
    var originalBalance = original.ChannelBalance ?? 50;
    var originalNoise = original.NoiseMode ?? QcyNoiseMode.NoiseCancellation;
    var originalPreset = original.EqualizerPreset ?? 0x01;
    var originalDynamicEqualizer = original.DynamicEqualizerEnabled ?? false;
    var originalLeftSingleTap = original.KeyFunctions.GetValueOrDefault((byte)0x01);
    QcyEqualizerBand[] customCurve =
    [
        .. QcyN65Equalizer.FactoryTuning.Bands.Take(6),
        new(8000, -3, 2.5, 0x03),
        new(64, 4, 0.7, QcyN65Equalizer.PeakFilter),
    ];

    await TestStepAsync(
        "balance 40",
        () => client.SetChannelBalanceAsync(40),
        () => client.State.ChannelBalance == 40);
    await TestStepAsync(
        $"balance {originalBalance}",
        () => client.SetChannelBalanceAsync(originalBalance),
        () => client.State.ChannelBalance == originalBalance);
    await TestStepAsync(
        "ANC transparency",
        () => client.SetNoiseModeAsync(QcyNoiseMode.Transparency),
        () => client.State.NoiseMode == QcyNoiseMode.Transparency);
    await TestStepAsync(
        "ANC off",
        () => client.SetNoiseModeAsync(QcyNoiseMode.Normal),
        () => client.State.NoiseMode == QcyNoiseMode.Normal);
    await TestStepAsync(
        $"ANC {originalNoise}",
        () => client.SetNoiseModeAsync(originalNoise),
        () => client.State.NoiseMode == originalNoise);
    await TestStepAsync(
        "EQ preset Pop",
        () => client.SetEqualizerPresetAsync(0x02),
        () => client.State.EqualizerPreset == 0x02);
    await TestStepAsync(
        "EQ custom curve (8 bands, shelf and Q changes)",
        () => client.SetN65EqualizerAsync(customCurve),
        () => client.State.EqualizerPreset == QcyN65Equalizer.CustomPreset &&
            client.State.Equalizer?.Bands.SequenceEqual(customCurve) == true);
    await TestStepAsync(
        $"EQ preset 0x{originalPreset:X2}",
        () => client.SetEqualizerPresetAsync(originalPreset),
        () => client.State.EqualizerPreset == originalPreset);
    await TestStepAsync(
        "gesture: single tap left -> volume up",
        () => client.SetN65KeyFunctionAsync(0x01, 0x05),
        () => client.State.KeyFunctions.GetValueOrDefault((byte)0x01) == 0x05);
    await TestStepAsync(
        $"gesture: single tap left -> 0x{originalLeftSingleTap:X2}",
        () => client.SetN65KeyFunctionAsync(0x01, originalLeftSingleTap),
        () => client.State.KeyFunctions.GetValueOrDefault((byte)0x01) == originalLeftSingleTap);
    await TestStepAsync(
        "dynamic EQ on",
        () => client.SetDynamicEqualizerAsync(true),
        () => client.State.DynamicEqualizerEnabled == true);
    await TestStepAsync(
        $"dynamic EQ {(originalDynamicEqualizer ? "on" : "off")}",
        () => client.SetDynamicEqualizerAsync(originalDynamicEqualizer),
        () => client.State.DynamicEqualizerEnabled == originalDynamicEqualizer);
}

if (willDisableWearDetection)
{
    Console.WriteLine("Disabling wear detection and waiting for N70 confirmation…");
    await client.SetWearDetectionAsync(false);
    Console.WriteLine($"Wear detection after confirmation: {BooleanText(client.State.WearDetectionEnabled)}");
}

return 0;

static async Task TestStepAsync(string name, Func<Task> action, Func<bool> isConfirmed)
{
    try
    {
        await action();
        Console.WriteLine($"{(isConfirmed() ? "ok    " : "failed")} {name}");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"failed {name}: {exception.Message}");
    }
}

static string Percent(byte? value) => value.HasValue ? $"{value}%" : "—";
static string YesNo(bool value) => value ? "yes" : "no";
static string BooleanText(bool? value) => value switch
{
    true => "enabled",
    false => "disabled",
    null => "not reported",
};
