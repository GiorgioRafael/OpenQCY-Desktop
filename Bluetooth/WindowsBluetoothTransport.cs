using System.Collections.Concurrent;
using OpenQCY_Desktop.Protocol;
using Windows.Devices.Enumeration;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Storage.Streams;

namespace OpenQCY_Desktop.Bluetooth;

public sealed class WindowsBluetoothTransport : IBluetoothTransport
{
    private const string BatteryLifeProperty = "System.Devices.BatteryLife";
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";

    public async Task<BluetoothBatteryInfo?> FindWindowsBatteryAsync(
        CancellationToken cancellationToken = default)
    {
        var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var properties = new[] { BatteryLifeProperty, IsConnectedProperty };
        var pairedDevices = await DeviceInformation.FindAllAsync(selector, properties);
        cancellationToken.ThrowIfCancellationRequested();

        return pairedDevices
            .Where(device => LooksLikeSupportedDevice(device.Name))
            .Select(device => new
            {
                device.Name,
                Battery = ReadByteProperty(device, BatteryLifeProperty),
                IsConnected = ReadBooleanProperty(device, IsConnectedProperty),
            })
            .Where(device => device.Battery is <= 100)
            .OrderByDescending(device => device.IsConnected)
            .Select(device => new BluetoothBatteryInfo(
                device.Name,
                device.Battery!.Value,
                device.IsConnected))
            .FirstOrDefault();
    }

    public async Task<IReadOnlyList<BluetoothDeviceInfo>> FindPairedQcyDevicesAsync(
        CancellationToken cancellationToken = default)
    {
        var devices = new List<BluetoothDeviceInfo>();

        // 1. Check Classic Bluetooth paired devices (e.g. QCY MeloBuds N30 connected for audio)
        var classicSelector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        var pairedClassicDevices = await DeviceInformation.FindAllAsync(classicSelector);
        foreach (var pairedClassic in pairedClassicDevices.Where(d => LooksLikeSupportedDevice(d.Name)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var addressStr = pairedClassic.Id.Contains('-')
                    ? pairedClassic.Id[(pairedClassic.Id.LastIndexOf('-') + 1)..].Replace(":", "")
                    : string.Empty;

                if (ulong.TryParse(addressStr, System.Globalization.NumberStyles.HexNumber, null, out var address))
                {
                    var vendorId = GuessVendorId(pairedClassic.Name);
                    devices.Add(new BluetoothDeviceInfo(
                        pairedClassic.Id,
                        pairedClassic.Name,
                        address,
                        address,
                        null,
                        vendorId,
                        short.MinValue,
                        0, 0, 0,
                        false, false, false,
                        DateTimeOffset.UtcNow));
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        // 2. Check GATT Services (A001 and A002)
        var serviceUuids = new[] { QcyUuids.MainService, QcyUuids.JieliMainService };
        foreach (var serviceUuid in serviceUuids)
        {
            var serviceSelector = GattDeviceService.GetDeviceSelectorFromUuid(serviceUuid);
            var knownServices = await DeviceInformation.FindAllAsync(serviceSelector);
            foreach (var knownService in knownServices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                GattDeviceService? service = null;
                try
                {
                    service = await GattDeviceService.FromIdAsync(knownService.Id);
                    cancellationToken.ThrowIfCancellationRequested();
                    if (service is null)
                    {
                        continue;
                    }

                    var device = await CreateKnownDeviceAsync(
                        service.Session.DeviceId.Id,
                        knownService.Name,
                        serviceUuid == QcyUuids.JieliMainService ? QcyUuids.N30VendorId : QcyUuids.N70BlackVendorId,
                        cancellationToken);
                    if (device is not null)
                    {
                        devices.Add(device);
                    }
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                }
                finally
                {
                    service?.Dispose();
                }
            }
        }

        // 3. Check Paired BLE devices matching name
        var pairedSelector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
        var pairedDevices = await DeviceInformation.FindAllAsync(pairedSelector);
        foreach (var pairedDevice in pairedDevices.Where(device => LooksLikeSupportedDevice(device.Name)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var vendorId = GuessVendorId(pairedDevice.Name);
                var device = await CreateKnownDeviceAsync(
                    pairedDevice.Id,
                    pairedDevice.Name,
                    vendorId,
                    cancellationToken);
                if (device is not null)
                {
                    devices.Add(device);
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }

        return devices
            .DistinctBy(device => device.BluetoothAddress)
            .ToArray();
    }

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
                    name = QcyUuids.GetModelName(advertisement.VendorId);
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
            .OrderByDescending(device => QcyUuids.IsSupported(device.VendorId))
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

        // 1. Try Classic Bluetooth RFCOMM SPP (for JieLi devices like MeloBuds N30)
        foreach (var address in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BluetoothDevice? classicDevice = null;
            try
            {
                classicDevice = await BluetoothDevice.FromBluetoothAddressAsync(address);
                if (classicDevice is not null)
                {
                    if (classicDevice.ConnectionStatus == BluetoothConnectionStatus.Connected)
                    {
                        var rfcomm = await classicDevice.GetRfcommServicesAsync(BluetoothCacheMode.Cached);
                        var spp = rfcomm.Services.FirstOrDefault(s => s.ServiceId.Uuid == QcyUuids.SppService);
                        foreach (var other in rfcomm.Services.Where(s => s != spp))
                        {
                            other.Dispose();
                        }

                        if (spp is not null)
                        {
                            return await RfcommBluetoothDeviceConnection.CreateAsync(classicDevice, spp, cancellationToken);
                        }
                    }
                    else
                    {
                        diagnostics.Add($"{classicDevice.Name} ({QcyAdvertisement.FormatAddress(address)}): Device is paired but disconnected. Make sure the earbuds are connected to Windows.");
                    }

                    classicDevice.Dispose();
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                classicDevice?.Dispose();
                diagnostics.Add($"{QcyAdvertisement.FormatAddress(address)} (RFCOMM): {exception.GetType().FullName}: {exception.Message} ({exception.HResult:X8})");
            }
        }

        // 2. Try BLE GATT (MainService A001 or JieliMainService A002)
        var servicesToTry = new[] { QcyUuids.MainService, QcyUuids.JieliMainService };

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
                    diagnostics.Add($"{QcyAdvertisement.FormatAddress(address)}: BLE device unavailable");
                    continue;
                }

                foreach (var serviceUuid in servicesToTry)
                {
                    var services = await bluetoothDevice.GetGattServicesForUuidAsync(
                        serviceUuid,
                        BluetoothCacheMode.Uncached);
                    cancellationToken.ThrowIfCancellationRequested();

                    if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
                    {
                        services = await bluetoothDevice.GetGattServicesForUuidAsync(
                            serviceUuid,
                            BluetoothCacheMode.Cached);
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    if (services.Status == GattCommunicationStatus.Success && services.Services.Count > 0)
                    {
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
                }

                diagnostics.Add($"{QcyAdvertisement.FormatAddress(address)}: Neither A001 nor A002 GATT service found");
                bluetoothDevice.Dispose();
                bluetoothDevice = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                bluetoothDevice?.Dispose();
                diagnostics.Add($"{QcyAdvertisement.FormatAddress(address)}: {exception.Message}");
            }
        }

        throw new InvalidOperationException(
            diagnostics.Count == 0
                ? "No QCY control address was advertised or reachable."
                : string.Join(Environment.NewLine, diagnostics));
    }

    internal static byte[] ReadBuffer(IBuffer buffer)
    {
        using var reader = DataReader.FromBuffer(buffer);
        var value = new byte[reader.UnconsumedBufferLength];
        reader.ReadBytes(value);
        return value;
    }

    private static bool LooksLikeSupportedDevice(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        (name.Contains("N70", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("N30", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("HT16", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("HT18", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("MeloBuds", StringComparison.OrdinalIgnoreCase)) &&
        (name.Contains("QCY", StringComparison.OrdinalIgnoreCase) ||
         name.Contains("MeloBuds", StringComparison.OrdinalIgnoreCase));

    private static ushort GuessVendorId(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return QcyUuids.N70BlackVendorId;
        }

        if (name.Contains("N30", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("HT16", StringComparison.OrdinalIgnoreCase))
        {
            return QcyUuids.N30VendorId;
        }

        return QcyUuids.N70BlackVendorId;
    }

    private static async Task<BluetoothDeviceInfo?> CreateKnownDeviceAsync(
        string deviceId,
        string? fallbackName,
        ushort vendorId,
        CancellationToken cancellationToken)
    {
        using var bluetoothDevice = await BluetoothLEDevice.FromIdAsync(deviceId);
        cancellationToken.ThrowIfCancellationRequested();
        if (bluetoothDevice is null)
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(bluetoothDevice.Name)
            ? fallbackName ?? QcyUuids.GetModelName(vendorId)
            : bluetoothDevice.Name;
        return new BluetoothDeviceInfo(
            deviceId,
            name,
            bluetoothDevice.BluetoothAddress,
            null,
            null,
            vendorId,
            short.MinValue,
            0,
            0,
            0,
            false,
            false,
            false,
            DateTimeOffset.UtcNow);
    }

    private static byte? ReadByteProperty(DeviceInformation device, string propertyName)
    {
        if (!device.Properties.TryGetValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        return value switch
        {
            byte byteValue => byteValue,
            ushort ushortValue when ushortValue <= byte.MaxValue => (byte)ushortValue,
            uint uintValue when uintValue <= byte.MaxValue => (byte)uintValue,
            int intValue when intValue is >= byte.MinValue and <= byte.MaxValue => (byte)intValue,
            _ => null,
        };
    }

    private static bool ReadBooleanProperty(DeviceInformation device, string propertyName) =>
        device.Properties.TryGetValue(propertyName, out var value) && value is true;
}
