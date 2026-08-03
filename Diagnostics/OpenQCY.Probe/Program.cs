using System.Text;
using OpenQCY_Desktop.Bluetooth;
using OpenQCY_Desktop.Device;
using OpenQCY_Desktop.Protocol;

Console.OutputEncoding = Encoding.UTF8;
var willDisableWearDetection = args.Contains("--disable-wear-detection", StringComparer.OrdinalIgnoreCase);
Console.WriteLine(willDisableWearDetection
    ? "OpenQCY Probe · diagnóstico e alteração solicitada da detecção de uso"
    : "OpenQCY Probe · diagnóstico local somente leitura");
Console.WriteLine("Abra o estojo e mantenha os dois fones próximos ao computador.");

var transport = new WindowsBluetoothTransport();
var devices = await transport.ScanForQcyDevicesAsync(TimeSpan.FromSeconds(12));
if (devices.Count == 0)
{
    Console.Error.WriteLine("Nenhum anúncio BLE QCY (0x521C) foi encontrado.");
    return 2;
}

foreach (var device in devices)
{
    Console.WriteLine(
        $"Encontrado: {device.Name} · vendor {device.VendorId} · RSSI {device.SignalStrength} dBm · " +
        $"L {device.LeftBattery}% / R {device.RightBattery}% / estojo {device.CaseBattery}%");
}

var target = devices.FirstOrDefault(device => QcyUuids.IsN70(device.VendorId)) ?? devices[0];
Console.WriteLine($"Conectando ao canal de controle de {target.Name}…");

await using var connection = await transport.ConnectAsync(target);
await using var client = await QcyDeviceClient.CreateAsync(connection);
client.ProtocolTrace += (_, line) => Console.WriteLine($"  {line}");
await client.RefreshAsync();

var state = client.State;
Console.WriteLine($"Conectado: {state.DeviceName}");
Console.WriteLine($"Firmware: {state.FirmwareVersion ?? "não informado"}");
Console.WriteLine($"Bateria: L {Percent(state.Battery.Left)} · R {Percent(state.Battery.Right)} · estojo {Percent(state.Battery.Case)}");
Console.WriteLine($"Detecção de uso: {BooleanText(state.WearDetectionEnabled)} · protocolo {state.WearDetectionProtocol}");
Console.WriteLine($"ANC: {state.NoiseMode?.ToString() ?? "não informado"}");
Console.WriteLine($"Modo jogo: {BooleanText(state.GameModeEnabled)}");
Console.WriteLine($"LDAC: {BooleanText(state.LdacEnabled)} · multiponto: {BooleanText(state.MultipointEnabled)}");
Console.WriteLine($"Equalizador: preset {state.EqualizerPreset?.ToString() ?? "não informado"} · {state.EqualizerGains.Count} bandas");
Console.WriteLine($"Características QCY encontradas: {connection.Characteristics.Count}");
foreach (var characteristic in connection.Characteristics.OrderBy(item => item.Uuid))
{
    Console.WriteLine(
        $"  {characteristic.Uuid:D} · leitura {YesNo(characteristic.CanRead)} · " +
        $"escrita {YesNo(characteristic.CanWrite)} · notificação {YesNo(characteristic.CanNotify)}");
}

if (willDisableWearDetection)
{
    Console.WriteLine("Desativando a detecção de uso e aguardando confirmação do N70…");
    await client.SetWearDetectionAsync(false);
    Console.WriteLine($"Detecção de uso após confirmação: {BooleanText(client.State.WearDetectionEnabled)}");
}

return 0;

static string Percent(byte? value) => value.HasValue ? $"{value}%" : "—";
static string YesNo(bool value) => value ? "sim" : "não";
static string BooleanText(bool? value) => value switch
{
    true => "ligada",
    false => "desligada",
    null => "não informada",
};
