namespace OpenQCY_Desktop.Bluetooth;

public interface IBluetoothDeviceConnection : IAsyncDisposable
{
    string Name { get; }
    ulong BluetoothAddress { get; }
    bool IsConnected { get; }
    IReadOnlyCollection<GattCharacteristicInfo> Characteristics { get; }

    event EventHandler? Disconnected;
    event EventHandler<GattValueChangedEventArgs>? ValueChanged;

    Task<byte[]> ReadAsync(Guid characteristicUuid, CancellationToken cancellationToken = default);
    Task WriteAsync(Guid characteristicUuid, ReadOnlyMemory<byte> value, CancellationToken cancellationToken = default);
    Task SubscribeAsync(Guid characteristicUuid, CancellationToken cancellationToken = default);
}

public sealed class GattValueChangedEventArgs(Guid characteristicUuid, byte[] value) : EventArgs
{
    public Guid CharacteristicUuid { get; } = characteristicUuid;
    public byte[] Value { get; } = value;
}
