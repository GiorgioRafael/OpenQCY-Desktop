using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using OpenQCY_Desktop.Protocol;

namespace OpenQCY_Desktop.Bluetooth;

public sealed class RfcommBluetoothDeviceConnection : IBluetoothDeviceConnection
{
    private readonly BluetoothDevice _device;
    private readonly RfcommDeviceService _service;
    private readonly StreamSocket _socket;
    private readonly DataWriter _writer;
    private readonly DataReader _reader;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private bool _readingStarted;
    private bool _disposed;

    private static readonly GattCharacteristicInfo[] VirtualCharacteristics =
    [
        new(QcyUuids.JieliCommand, CanRead: false, CanWrite: true, CanNotify: false),
        new(QcyUuids.JieliNotification, CanRead: false, CanWrite: false, CanNotify: true),
    ];

    private RfcommBluetoothDeviceConnection(
        BluetoothDevice device,
        RfcommDeviceService service,
        StreamSocket socket)
    {
        _device = device;
        _service = service;
        _socket = socket;
        _writer = new DataWriter(socket.OutputStream);
        _reader = new DataReader(socket.InputStream)
        {
            InputStreamOptions = InputStreamOptions.Partial,
        };

        _device.ConnectionStatusChanged += Device_ConnectionStatusChanged;
    }

    public string Name => string.IsNullOrWhiteSpace(_device.Name) ? "QCY Device" : _device.Name;
    public ulong BluetoothAddress => _device.BluetoothAddress;
    public bool IsConnected => _device.ConnectionStatus == BluetoothConnectionStatus.Connected;

    public IReadOnlyCollection<GattCharacteristicInfo> Characteristics => VirtualCharacteristics;

    public event EventHandler? Disconnected;
    public event EventHandler<GattValueChangedEventArgs>? ValueChanged;

    public static async Task<RfcommBluetoothDeviceConnection> CreateAsync(
        BluetoothDevice device,
        RfcommDeviceService service,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(service);

        var socket = new StreamSocket();
        try
        {
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName)
                .AsTask(cancellationToken);
            return new RfcommBluetoothDeviceConnection(device, service, socket);
        }
        catch
        {
            socket.Dispose();
            service.Dispose();
            device.Dispose();
            throw;
        }
    }

    public Task<byte[]> ReadAsync(Guid characteristicUuid, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Direct GATT read is not supported over RFCOMM SPP.");

    public async Task WriteAsync(
        Guid characteristicUuid,
        ReadOnlyMemory<byte> value,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            _writer.WriteBytes(value.ToArray());
            await _writer.StoreAsync().AsTask(cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task SubscribeAsync(Guid characteristicUuid, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_readingStarted)
        {
            _readingStarted = true;
            _ = ReadLoopAsync(_cts.Token);
        }

        return Task.CompletedTask;
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && !_disposed)
            {
                var loaded = await _reader.LoadAsync(1024).AsTask(cancellationToken);
                if (loaded == 0)
                {
                    break;
                }

                var bytes = new byte[loaded];
                _reader.ReadBytes(bytes);
                ValueChanged?.Invoke(this, new GattValueChangedEventArgs(QcyUuids.JieliNotification, bytes));
            }
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested || _disposed)
        {
            // Normal shutdown
        }
        catch (Exception)
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Device_ConnectionStatusChanged(BluetoothDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _device.ConnectionStatusChanged -= Device_ConnectionStatusChanged;
        _cts.Cancel();
        _cts.Dispose();

        _writeLock.Dispose();
        _writer.Dispose();
        _reader.Dispose();
        _socket.Dispose();
        _service.Dispose();
        _device.Dispose();

        await Task.CompletedTask;
    }
}
