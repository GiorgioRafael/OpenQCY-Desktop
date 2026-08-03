using System.Collections.Concurrent;
using OpenQCY_Desktop.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace OpenQCY_Desktop.Bluetooth;

public sealed class WindowsBluetoothTransport : IBluetoothTransport
{
    public Task<IReadOnlyList<BluetoothDeviceInfo>> FindPairedQcyDevicesAsync(
        CancellationToken cancellationToken = default) =>
        ScanForQcyDevicesAsync(TimeSpan.FromSeconds(6), cancellationToken);

    public async Task<IReadOnlyList<BluetoothDeviceInfo>> ScanForQcyDevicesAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        var devices = new ConcurrentDictionary<ulong, BluetoothDeviceInfo>();
        var watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Active,
            AllowExtendedAdvertisements = true,
        };

        void OnReceived(BluetoothLEAdvertisementWatcher _, BluetoothLEAdvertisementReceivedEventArgs eventArgs)
        {
            foreach (var manufacturerData in eventArgs.Advertisement.ManufacturerData)
            {
                if (manufacturerData.CompanyId != QcyUuids.CompanyId)
                {
                    continue;
                }

                var advertisement = QcyAdvertisement.Parse(ReadBuffer(manufacturerData.Data));
                if (advertisement is null)
                {
                    continue;
                }

                var name = eventArgs.Advertisement.LocalName;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = QcyUuids.IsN70(advertisement.VendorId)
                        ? "QCY MeloBuds N70"
                        : $"QCY device ({advertisement.VendorId})";
                }

                var info = new BluetoothDeviceInfo(
                    QcyAdvertisement.FormatAddress(eventArgs.BluetoothAddress),
                    name,
                    eventArgs.BluetoothAddress,
                    advertisement.ControlAddress,
                    advertisement.OtherAddress,
                    advertisement.VendorId,
                    eventArgs.RawSignalStrengthInDBm,
                    advertisement.LeftBattery,
                    advertisement.RightBattery,
                    advertisement.CaseBattery,
                    advertisement.LeftCharging,
                    advertisement.RightCharging,
                    advertisement.CaseCharging,
                    eventArgs.Timestamp);

                devices.AddOrUpdate(eventArgs.BluetoothAddress, info, (_, previous) =>
                    info with
                    {
                        Name = string.IsNullOrWhiteSpace(info.Name) ? previous.Name : info.Name,
                        ControlAddress = info.ControlAddress ?? previous.ControlAddress,
                        OtherAddress = info.OtherAddress ?? previous.OtherAddress,
                    });
            }
        }

        watcher.Received += OnReceived;
        try
        {
            watcher.Start();
            await Task.Delay(duration, cancellationToken);
        }
        finally
        {
            watcher.Stop();
            watcher.Received -= OnReceived;
        }

        return devices.Values
            .OrderByDescending(device => QcyUuids.IsN70(device.VendorId))
            .ThenByDescending(device => device.SignalStrength)
            .ToArray();
    }

    public async Task<IBluetoothDeviceConnection> ConnectAsync(
        BluetoothDeviceInfo device,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var candidates = new ulong?[]
            {
                device.ControlAddress,
                device.BluetoothAddress,
                device.OtherAddress,
            }
            .Where(address => address.HasValue)
            .Select(address => address!.Value)
            .Distinct()
            .ToArray();

        var diagnostics = new List<string>();
        foreach (var address in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BluetoothLEDevice? bluetoothDevice = null;
            try
            {
                bluetoothDevice = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
                cancellationToken.ThrowIfCancellationRequested();
                if (bluetoothDevice is null)
                {
                    diagnostics.Add($"{QcyAdvertisement.FormatAddress(address)}: dispositivo BLE indisponível");
                    continue;
                }

                var services = await bluetoothDevice.GetGattServicesForUuidAsync(
                    QcyUuids.MainService,
                    BluetoothCacheMode.Uncached);
                cancellationToken.ThrowIfCancellationRequested();

                if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
                {
                    diagnostics.Add($"{QcyAdvertisement.FormatAddress(address)}: serviço A001 não encontrado ({services.Status})");
                    bluetoothDevice.Dispose();
                    bluetoothDevice = null;
                    continue;
                }

                var service = services.Services[0];
                for (var index = 1; index < services.Services.Count; index++)
                {
                    services.Services[index].Dispose();
                }

                return await WindowsBluetoothDeviceConnection.CreateAsync(
                    bluetoothDevice,
                    service,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                bluetoothDevice?.Dispose();
                diagnostics.Add($"{QcyAdvertisement.FormatAddress(address)}: {exception.Message}");
            }
        }

        throw new InvalidOperationException(
            diagnostics.Count == 0
                ? "Nenhum endereço de controle QCY foi anunciado."
                : string.Join(Environment.NewLine, diagnostics));
    }

    internal static byte[] ReadBuffer(IBuffer buffer)
    {
        using var reader = DataReader.FromBuffer(buffer);
        var value = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(value);
        return value;
    }
}
