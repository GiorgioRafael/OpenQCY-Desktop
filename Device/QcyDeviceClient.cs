using System.Buffers.Binary;
using OpenQCY_Desktop.Bluetooth;
using OpenQCY_Desktop.Protocol;

namespace OpenQCY_Desktop.Device;

public sealed class QcyDeviceClient : IAsyncDisposable
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(2.5);

    private readonly IBluetoothDeviceConnection _connection;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<byte, TaskCompletionSource<byte[]>> _pendingResponses = [];
    private bool _disposed;

    private QcyDeviceClient(IBluetoothDeviceConnection connection)
    {
        _connection = connection;
        State = new QcyDeviceState
        {
            IsConnected = connection.IsConnected,
            DeviceName = connection.Name,
        };

        _connection.ValueChanged += Connection_ValueChanged;
        _connection.Disconnected += Connection_Disconnected;
    }

    public QcyDeviceState State { get; private set; }

    public event EventHandler<QcyDeviceState>? StateChanged;
    public event EventHandler<string>? ProtocolTrace;

    public static async Task<QcyDeviceClient> CreateAsync(
        IBluetoothDeviceConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var client = new QcyDeviceClient(connection);
        try
        {
            await client.InitializeAsync(cancellationToken);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await ReadDirectCharacteristicsAsync(cancellationToken);
        await QueryAsync(0x2C, cancellationToken);
        if (State.WearDetectionProtocol == QcyWearDetectionProtocol.Unknown)
        {
            await QueryAsync(0x06, cancellationToken);
        }

        foreach (var opcode in new byte[]
        {
            0x17, 0x09, 0x10, 0x23, 0x24, 0x2A, 0x1D, 0x14,
        })
        {
            await QueryAsync(opcode, cancellationToken);
        }

        if (State.Battery.Left is null)
        {
            await QueryAsync(0x2F, cancellationToken);
        }

        if (State.FirmwareVersion is null)
        {
            await QueryAsync(0x30, cancellationToken);
        }

        if (!_connection.Characteristics.Any(info => info.Uuid == QcyUuids.Equalizer))
        {
            await QueryAsync(0x22, cancellationToken);
        }

        if (State.KeyFunctions.Count == 0)
        {
            await QueryAsync(0x2B, cancellationToken);
        }
    }

    public async Task SetWearDetectionAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        byte opcode;
        byte[] packet;
        var state = State;

        if (state.WearDetectionProtocol == QcyWearDetectionProtocol.WearingDetection &&
            state.WearingDetection is not null)
        {
            opcode = QcyCommands.WearingDetectionOpcode;
            packet = QcyCommands.SetWearingDetection(enabled, state.WearingDetection);
        }
        else if (state.WearDetectionProtocol == QcyWearDetectionProtocol.Legacy)
        {
            opcode = QcyCommands.InEarDetectionOpcode;
            packet = QcyCommands.SetInEarDetection(enabled);
        }
        else
        {
            throw new NotSupportedException(
                "The N70 did not confirm a compatible wear-detection command on this connection.");
        }

        await WriteAndConfirmAsync(opcode, packet, cancellationToken);
    }

    public async Task SetNoiseModeAsync(
        QcyNoiseMode mode,
        QcyNoiseCancellationMode cancellationMode = QcyNoiseCancellationMode.Adaptive,
        CancellationToken cancellationToken = default)
    {
        var expected = QcyNoiseControlState.Create(mode, cancellationMode);
        var response = await SendAndAwaitAsync(
            0x17,
            QcyCommands.SetNoiseMode(mode, cancellationMode),
            cancellationToken);
        var confirmed = response is null ? null : QcyNoiseControlState.Parse(response);
        if (confirmed != expected)
        {
            response = await QueryAsync(0x17, cancellationToken);
            confirmed = response is null ? null : QcyNoiseControlState.Parse(response);
        }

        if (confirmed != expected)
        {
            throw new InvalidOperationException(
                $"The N70 reported {FormatNoiseState(confirmed)}, but the app requested {FormatNoiseState(expected)}.");
        }
    }

    public Task SetGameModeAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAndConfirmAsync(0x09, QcyCommands.SetGameMode(enabled), cancellationToken);

    public Task SetSleepModeAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAndConfirmAsync(0x10, QcyCommands.SetSleepMode(enabled), cancellationToken);

    public Task SetLdacAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAndConfirmAsync(0x23, QcyCommands.SetLdac(enabled), cancellationToken);

    public Task SetMultipointAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAndConfirmAsync(0x24, QcyCommands.SetMultipoint(enabled), cancellationToken);

    public Task SetWindDetectionAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAndConfirmAsync(0x2A, QcyCommands.SetWindDetection(enabled), cancellationToken);

    public Task SetPromptVolumeAsync(double percentage, CancellationToken cancellationToken = default)
    {
        var maximum = State.PromptVolumeMaximum;
        var value = (byte)Math.Clamp((int)Math.Round(percentage / 100 * maximum), 0, maximum);
        return WriteAndConfirmAsync(0x1D, QcyCommands.SetPromptVolume(value), cancellationToken);
    }

    public Task SetAutoPowerOffAsync(ushort minutes, CancellationToken cancellationToken = default) =>
        WriteAndConfirmAsync(0x14, QcyCommands.SetAutoPowerOff(minutes), cancellationToken);

    public async Task SetEqualizerPresetAsync(byte presetIndex, CancellationToken cancellationToken = default)
    {
        await _connection.WriteAsync(QcyUuids.Equalizer, QcyCommands.SetEqualizerPreset(presetIndex), cancellationToken);
        await QueryAsync(0x22, cancellationToken);
    }

    public async Task SetCustomEqualizerAsync(
        IReadOnlyList<double> gains,
        CancellationToken cancellationToken = default)
    {
        await WriteAndConfirmAsync(0x22, QcyCommands.BuildCustomEqualizer(gains), cancellationToken);
    }

    public async Task SetKeyFunctionsAsync(
        IReadOnlyDictionary<byte, byte> mappings,
        CancellationToken cancellationToken = default)
    {
        await _connection.WriteAsync(QcyUuids.KeyFunctions, QcyCommands.SetKeyFunctions(mappings), cancellationToken);
        await ReadKeyFunctionsAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection.ValueChanged -= Connection_ValueChanged;
        _connection.Disconnected -= Connection_Disconnected;
        lock (_stateLock)
        {
            foreach (var pending in _pendingResponses.Values)
            {
                pending.TrySetCanceled();
            }

            _pendingResponses.Clear();
        }

        _commandLock.Dispose();
        await _connection.DisposeAsync();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        RequireCharacteristic(QcyUuids.Command, canWrite: true);
        RequireCharacteristic(QcyUuids.Notification, canNotify: true);
        await _connection.SubscribeAsync(QcyUuids.Notification, cancellationToken);
        if (_connection.Characteristics.Any(info => info.Uuid == QcyUuids.Battery && info.CanNotify))
        {
            await _connection.SubscribeAsync(QcyUuids.Battery, cancellationToken);
        }

        await RefreshAsync(cancellationToken);
    }

    private async Task ReadDirectCharacteristicsAsync(CancellationToken cancellationToken)
    {
        await RefreshBatteryAsync(cancellationToken);

        var version = await TryReadAsync(QcyUuids.FirmwareVersion, cancellationToken);
        if (version is { Length: >= 3 })
        {
            UpdateState(state => state with { FirmwareVersion = FormatVersion(version) });
        }

        await ReadKeyFunctionsAsync(cancellationToken);

        var equalizer = await TryReadAsync(QcyUuids.Equalizer, cancellationToken);
        if (equalizer is { Length: > 0 })
        {
            UpdateState(state => state with { EqualizerPreset = equalizer[0] });
        }
    }

    public async Task RefreshBatteryAsync(CancellationToken cancellationToken = default)
    {
        var battery = await TryReadAsync(QcyUuids.Battery, cancellationToken);
        if (battery is { Length: >= 2 })
        {
            UpdateState(state => state with { Battery = ParseBattery(battery) });
            return;
        }

        await QueryAsync(0x2F, cancellationToken);
    }

    private async Task ReadKeyFunctionsAsync(CancellationToken cancellationToken)
    {
        var keyFunctions = await TryReadAsync(QcyUuids.KeyFunctions, cancellationToken);
        if (keyFunctions is not null)
        {
            UpdateState(state => state with
            {
                KeyFunctions = QcyCommands.ParseKeyFunctions(keyFunctions),
            });
        }
    }

    private async Task<byte[]?> TryReadAsync(Guid characteristic, CancellationToken cancellationToken)
    {
        if (!_connection.Characteristics.Any(info => info.Uuid == characteristic && info.CanRead))
        {
            return null;
        }

        try
        {
            return await _connection.ReadAsync(characteristic, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Trace($"read {ShortUuid(characteristic)} failed: {exception.Message}");
            return null;
        }
    }

    private async Task<byte[]?> QueryAsync(byte opcode, CancellationToken cancellationToken)
    {
        var response = await SendAndAwaitAsync(opcode, QcyCommands.Request(opcode), cancellationToken);
        if (response is null)
        {
            Trace($"query 0x{opcode:X2}: no response");
        }

        return response;
    }

    private async Task WriteAndConfirmAsync(byte opcode, byte[] packet, CancellationToken cancellationToken)
    {
        var response = await SendAndAwaitAsync(opcode, packet, cancellationToken);
        if (response is null)
        {
            response = await QueryAsync(opcode, cancellationToken);
        }

        if (response is null)
        {
            throw new InvalidOperationException(
                $"The N70 did not confirm command 0x{opcode:X2}; the change was not considered applied.");
        }
    }

    private async Task<byte[]?> SendAndAwaitAsync(
        byte expectedOpcode,
        byte[] packet,
        CancellationToken cancellationToken)
    {
        await _commandLock.WaitAsync(cancellationToken);
        TaskCompletionSource<byte[]> completion;
        try
        {
            completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_stateLock)
            {
                _pendingResponses[expectedOpcode] = completion;
            }

            Trace($"tx {Convert.ToHexString(packet)}");
            await _connection.WriteAsync(QcyUuids.Command, packet, cancellationToken);

            try
            {
                return await completion.Task.WaitAsync(ResponseTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                return null;
            }
        }
        finally
        {
            lock (_stateLock)
            {
                _pendingResponses.Remove(expectedOpcode);
            }

            _commandLock.Release();
        }
    }

    private void Connection_ValueChanged(object? sender, GattValueChangedEventArgs eventArgs)
    {
        if (eventArgs.CharacteristicUuid == QcyUuids.Battery)
        {
            if (eventArgs.Value.Length >= 2)
            {
                UpdateState(state => state with { Battery = ParseBattery(eventArgs.Value) });
            }

            return;
        }

        if (eventArgs.CharacteristicUuid != QcyUuids.Notification)
        {
            return;
        }

        Trace($"rx {Convert.ToHexString(eventArgs.Value)}");
        foreach (var command in QcyPacket.Parse(eventArgs.Value))
        {
            ApplyCommand(command);
            TaskCompletionSource<byte[]>? completion;
            lock (_stateLock)
            {
                _pendingResponses.TryGetValue(command.Opcode, out completion);
            }

            completion?.TrySetResult(command.Parameters);
        }
    }

    private void Connection_Disconnected(object? sender, EventArgs eventArgs)
    {
        UpdateState(state => state with { IsConnected = false });
        lock (_stateLock)
        {
            foreach (var pending in _pendingResponses.Values)
            {
                pending.TrySetException(new InvalidOperationException("The N70 disconnected during the command."));
            }
        }
    }

    private void ApplyCommand(QcyCommand command)
    {
        var parameters = command.Parameters;
        switch (command.Opcode)
        {
            case 0x06 when parameters.Length >= 1:
                UpdateState(state => state.WearDetectionProtocol == QcyWearDetectionProtocol.WearingDetection
                    ? state
                    : state with
                    {
                        WearDetectionEnabled = parameters[0] == 0x01,
                        WearDetectionProtocol = QcyWearDetectionProtocol.Legacy,
                    });
                break;
            case 0x2C:
                var wearing = QcyWearingDetection.Parse(parameters);
                if (wearing is not null)
                {
                    UpdateState(state => state with
                    {
                        WearDetectionEnabled = wearing.Enabled,
                        WearDetectionProtocol = QcyWearDetectionProtocol.WearingDetection,
                        WearingDetection = wearing,
                    });
                }
                break;
            case 0x17 when parameters.Length >= 3:
                var noiseControl = QcyNoiseControlState.Parse(parameters);
                if (noiseControl is not null)
                {
                    UpdateState(state => state with { NoiseControl = noiseControl });
                }
                break;
            case 0x09 when parameters.Length >= 1:
                UpdateState(state => state with { GameModeEnabled = parameters[0] == 0x01 });
                break;
            case 0x10 when parameters.Length >= 1:
                UpdateState(state => state with { SleepModeEnabled = parameters[0] == 0x01 });
                break;
            case 0x23 when parameters.Length >= 1:
                UpdateState(state => state with { LdacEnabled = parameters[0] == 0x01 });
                break;
            case 0x24 when parameters.Length >= 1:
                UpdateState(state => state with { MultipointEnabled = parameters[0] == 0x01 });
                break;
            case 0x2A when parameters.Length >= 1:
                UpdateState(state => state with { WindDetectionEnabled = parameters[0] == 0x01 });
                break;
            case 0x1D when parameters.Length >= 1:
                UpdateState(state => state with
                {
                    PromptVolume = parameters[0],
                    PromptVolumeMaximum = parameters.Length >= 2 && parameters[1] > 0
                        ? parameters[1]
                        : state.PromptVolumeMaximum,
                });
                break;
            case 0x14 when parameters.Length >= 2:
                UpdateState(state => state with
                {
                    AutoPowerOffMinutes = BinaryPrimitives.ReadUInt16LittleEndian(parameters),
                });
                break;
            case 0x2F when parameters.Length >= 2:
                UpdateState(state => state with { Battery = ParseBattery(parameters) });
                break;
            case 0x30 when parameters.Length >= 3:
                UpdateState(state => state with { FirmwareVersion = FormatVersion(parameters) });
                break;
            case 0x22:
                var equalizer = QcyEqualizerState.ParseV2(parameters);
                if (equalizer is not null)
                {
                    UpdateState(state => state with
                    {
                        EqualizerPreset = equalizer.PresetIndex,
                        EqualizerGains = equalizer.Bands.Select(band => band.Gain).ToArray(),
                    });
                }
                break;
            case 0x2B when parameters.Length >= 2:
                UpdateState(state => state with { KeyFunctions = QcyCommands.ParseKeyFunctions(parameters) });
                break;
        }
    }

    private void RequireCharacteristic(Guid uuid, bool canWrite = false, bool canNotify = false)
    {
        var characteristic = _connection.Characteristics.FirstOrDefault(info => info.Uuid == uuid);
        if (characteristic is null || (canWrite && !characteristic.CanWrite) || (canNotify && !characteristic.CanNotify))
        {
            throw new NotSupportedException($"Required QCY channel {uuid:D} is unavailable.");
        }
    }

    private void UpdateState(Func<QcyDeviceState, QcyDeviceState> update)
    {
        QcyDeviceState state;
        lock (_stateLock)
        {
            State = update(State);
            state = State;
        }

        StateChanged?.Invoke(this, state);
    }

    private void Trace(string message) => ProtocolTrace?.Invoke(this, message);

    private static QcyBatteryState ParseBattery(ReadOnlySpan<byte> parameters)
    {
        static byte Level(byte value) => (byte)Math.Min(value & 0x7F, 100);
        var left = parameters.Length > 0 ? Level(parameters[0]) : (byte?)null;
        var right = parameters.Length > 1 ? Level(parameters[1]) : (byte?)null;
        var deviceCase = parameters.Length > 2 && (parameters[2] & 0x7F) > 0
            ? Level(parameters[2])
            : (byte?)null;
        return new QcyBatteryState(
            left,
            right,
            deviceCase,
            parameters.Length > 0 && (parameters[0] & 0x80) != 0,
            parameters.Length > 1 && (parameters[1] & 0x80) != 0,
            parameters.Length > 2 && (parameters[2] & 0x80) != 0);
    }

    private static string FormatNoiseState(QcyNoiseControlState? state) =>
        state is null
            ? "an unknown state"
            : Convert.ToHexString(state.ToParameters());

    private static string FormatVersion(ReadOnlySpan<byte> value) =>
        value.Length >= 6
            ? $"L {value[0]}.{value[1]}.{value[2]} · R {value[3]}.{value[4]}.{value[5]}"
            : $"{value[0]}.{value[1]}.{value[2]}";

    private static string ShortUuid(Guid uuid) => uuid.ToString("D")[4..8];
}
