using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using WindowsGattValueChangedEventArgs = Windows.Devices.Bluetooth.GenericAttributeProfile.GattValueChangedEventArgs;

namespace OpenQCY_Desktop.Bluetooth;

internal sealed class WindowsBluetoothDeviceConnection : IBluetoothDeviceConnection
{
    private readonly BluetoothLEDevice _device;
    private readonly GattDeviceService _service;
    private readonly GattSession _session;
    private readonly Dictionary<Guid, GattCharacteristic> _characteristics;
    private readonly HashSet<Guid> _subscriptions = [];
    private bool _disposed;

    private WindowsBluetoothDeviceConnection(
        BluetoothLEDevice device,
        GattDeviceService service,
        Dictionary<Guid, GattCharacteristic> characteristics)
    {
        _device = device;
        _service = service;
        _session = service.Session;
        _characteristics = characteristics;
        _session.MaintainConnection = true;
        _device.ConnectionStatusChanged += Device_ConnectionStatusChanged;
    }

    public string Name => string.IsNullOrWhiteSpace(_device.Name) ? "QCY device" : _device.Name;
    public ulong BluetoothAddress => _device.BluetoothAddress;
    public bool IsConnected => _device.ConnectionStatus == BluetoothConnectionStatus.Connected;

    public IReadOnlyCollection<GattCharacteristicInfo> Characteristics => _characteristics.Values
        .Select(characteristic => new GattCharacteristicInfo(
            characteristic.Uuid,
            characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Read),
            characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Write) ||
                characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse),
            characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify) ||
                characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Indicate)))
        .ToArray();

    public event EventHandler? Disconnected;
    public event EventHandler<GattValueChangedEventArgs>? ValueChanged;

    public static async Task<WindowsBluetoothDeviceConnection> CreateAsync(
        BluetoothLEDevice device,
        GattDeviceService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.GetCharacteristicsAsync(BluetoothCacheMode.Uncached);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Status != GattCommunicationStatus.Success)
            {
                throw new InvalidOperationException($"Não foi possível enumerar características QCY: {result.Status}.");
            }

            return new WindowsBluetoothDeviceConnection(
                device,
                service,
                result.Characteristics.ToDictionary(characteristic => characteristic.Uuid));
        }
        catch
        {
            service.Dispose();
            device.Dispose();
            throw;
        }
    }

    public async Task<byte[]> ReadAsync(Guid characteristicUuid, CancellationToken cancellationToken = default)
    {
        var characteristic = GetCharacteristic(characteristicUuid);
        var result = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Leitura GATT {characteristicUuid:D} falhou: {result.Status}.");
        }

        return WindowsBluetoothTransport.ReadBuffer(result.Value);
    }

    public async Task WriteAsync(
        Guid characteristicUuid,
        ReadOnlyMemory<byte> value,
        CancellationToken cancellationToken = default)
    {
        var characteristic = GetCharacteristic(characteristicUuid);
        using var writer = new DataWriter();
        writer.WriteBytes(value.ToArray());
        var buffer = writer.DetachBuffer();
        var option = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.WriteWithoutResponse)
            ? GattWriteOption.WriteWithoutResponse
            : GattWriteOption.WriteWithResponse;

        var result = await characteristic.WriteValueWithResultAsync(buffer, option);
        cancellationToken.ThrowIfCancellationRequested();
        if (result.Status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Escrita GATT {characteristicUuid:D} falhou: {result.Status}.");
        }
    }

    public async Task SubscribeAsync(Guid characteristicUuid, CancellationToken cancellationToken = default)
    {
        var characteristic = GetCharacteristic(characteristicUuid);
        if (!_subscriptions.Add(characteristicUuid))
        {
            return;
        }

        characteristic.ValueChanged += Characteristic_ValueChanged;
        var descriptorValue = characteristic.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify)
            ? GattClientCharacteristicConfigurationDescriptorValue.Notify
            : GattClientCharacteristicConfigurationDescriptorValue.Indicate;

        var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(descriptorValue);
        cancellationToken.ThrowIfCancellationRequested();
        if (status != GattCommunicationStatus.Success)
        {
            characteristic.ValueChanged -= Characteristic_ValueChanged;
            _subscriptions.Remove(characteristicUuid);
            throw new InvalidOperationException($"Assinatura GATT {characteristicUuid:D} falhou: {status}.");
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _device.ConnectionStatusChanged -= Device_ConnectionStatusChanged;
        foreach (var uuid in _subscriptions)
        {
            if (_characteristics.TryGetValue(uuid, out var characteristic))
            {
                characteristic.ValueChanged -= Characteristic_ValueChanged;
            }
        }

        _subscriptions.Clear();
        _session.MaintainConnection = false;
        _service.Dispose();
        _device.Dispose();
        return ValueTask.CompletedTask;
    }

    private GattCharacteristic GetCharacteristic(Guid uuid)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _characteristics.TryGetValue(uuid, out var characteristic)
            ? characteristic
            : throw new NotSupportedException($"O N70 não expôs a característica {uuid:D}.");
    }

    private void Characteristic_ValueChanged(GattCharacteristic sender, WindowsGattValueChangedEventArgs args)
    {
        ValueChanged?.Invoke(this, new GattValueChangedEventArgs(
            sender.Uuid,
            WindowsBluetoothTransport.ReadBuffer(args.CharacteristicValue)));
    }

    private void Device_ConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }
}
