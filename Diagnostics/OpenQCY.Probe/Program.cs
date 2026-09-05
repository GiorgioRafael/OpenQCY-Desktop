using System.Text;
using OpenQCY_Desktop.Bluetooth;
using OpenQCY_Desktop.Device;
using OpenQCY_Desktop.Protocol;

Console.OutputEncoding = Encoding.UTF8;
var willDisableWearDetection = args.Contains("--disable-wear-detection", StringComparer.OrdinalIgnoreCase);
var windowsBatteryOnly = args.Contains("--windows-battery", StringComparer.OrdinalIgnoreCase);
Console.WriteLine(willDisableWearDetection
    ? "OpenQCY Probe · diagnostics and requested wear-detection change"
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

var target = devices.FirstOrDefault(device => QcyUuids.IsN70(device.VendorId)) ?? devices[0];
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
Console.WriteLine($"Equalizer: preset {state.EqualizerPreset?.ToString() ?? "not reported"} · {state.EqualizerGains.Count} bands");
Console.WriteLine($"QCY characteristics found: {connection.Characteristics.Count}");
foreach (var characteristic in connection.Characteristics.OrderBy(item => item.Uuid))
{
    Console.WriteLine(
        $"  {characteristic.Uuid:D} · read {YesNo(characteristic.CanRead)} · " +
        $"write {YesNo(characteristic.CanWrite)} · notify {YesNo(characteristic.CanNotify)}");
}

if (willDisableWearDetection)
{
    Console.WriteLine("Disabling wear detection and waiting for N70 confirmation…");
    await client.SetWearDetectionAsync(false);
    Console.WriteLine($"Wear detection after confirmation: {BooleanText(client.State.WearDetectionEnabled)}");
}

return 0;

static string Percent(byte? value) => value.HasValue ? $"{value}%" : "—";
static string YesNo(bool value) => value ? "yes" : "no";
static string BooleanText(bool? value) => value switch
{
    true => "enabled",
    false => "disabled",
    null => "not reported",
};
