using System.Buffers.Binary;
using OpenQCY_Desktop.Bluetooth;
using OpenQCY_Desktop.Protocol;

namespace OpenQCY_Desktop.Device;

public sealed class QcyDeviceClient : IAsyncDisposable
{
    private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan NoiseTransition = TimeSpan.FromSeconds(3);

    // Opcodes validated only on the N70. The N65 pushes some of them with other meanings (0x10).
    private static readonly HashSet<byte> N70OnlyOpcodes = [0x06, 0x09, 0x10, 0x14, 0x1D, 0x24, 0x2A, 0x2C];

    private readonly IBluetoothDeviceConnection _connection;
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly object _stateLock = new();
    private readonly Dictionary<byte, TaskCompletionSource<byte[]>> _pendingResponses = [];
    private readonly Dictionary<byte, TaskCompletionSource<byte>> _pendingAcknowledgements = [];
    private readonly bool _isJieli;
    private readonly Guid _commandUuid;
    private readonly Guid _notificationUuid;
    private byte _sequence;
    private DateTimeOffset _lastNoiseChange = DateTimeOffset.MinValue;
    private bool _disposed;

    private QcyDeviceClient(IBluetoothDeviceConnection connection)
    {
        _connection = connection;
        _isJieli = connection.ServiceUuid == QcyUuids.JieliService;
        _commandUuid = _isJieli ? QcyUuids.JieliCommand : QcyUuids.Command;
        _notificationUuid = _isJieli ? QcyUuids.JieliNotification : QcyUuids.Notification;
        State = new QcyDeviceState
        {
            IsConnected = connection.IsConnected,
            DeviceName = connection.Name,
            ProtocolVariant = _isJieli ? QcyProtocolVariant.Jieli : QcyProtocolVariant.Standard,
        };

        _connection.ValueChanged += Connection_ValueChanged;
        _connection.Disconnected += Connection_Disconnected;
    }

    public QcyDeviceState State { get; private set; }
    public bool IsJieli => _isJieli;

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
        if (_isJieli)
        {
            foreach (var opcode in new byte[]
            {
                0x30, 0x2F, 0x17, 0x22, 0x23, 0x4A, 0x27, 0x16, 0x2B,
            })
            {
                await QueryAsync(opcode, cancellationToken);
            }

            return;
        }

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
                "The earbuds did not confirm a compatible wear-detection command on this connection.");
        }

        await WriteAndConfirmAsync(opcode, packet, cancellationToken);
    }

    public async Task SetNoiseModeAsync(
        QcyNoiseMode mode,
        QcyNoiseCancellationMode cancellationMode = QcyNoiseCancellationMode.Adaptive,
        CancellationToken cancellationToken = default)
    {
        var expected = QcyNoiseControlState.Create(mode, cancellationMode);
        if (_isJieli)
        {
            await WriteNoiseModeAsync(expected, cancellationToken);
            return;
        }

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
                $"The earbuds reported {FormatNoiseState(confirmed)}, but the app requested {FormatNoiseState(expected)}.");
        }
    }

    public Task SetGameModeAsync(bool enabled, CancellationToken cancellationToken = default) =>
        _isJieli
            ? WriteBeforeRestartAsync(
                0x4A,
                QcyCommands.SetN65GameMode(enabled),
                state => state with { GameModeEnabled = enabled },
                cancellationToken)
            : WriteAndConfirmAsync(0x09, QcyCommands.SetGameMode(enabled), cancellationToken);

    public Task SetDynamicEqualizerAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAndConfirmAsync(0x27, QcyCommands.SetDynamicEqualizer(enabled), cancellationToken);

    public async Task SetN65KeyFunctionAsync(
        byte gesture,
        byte function,
        CancellationToken cancellationToken = default)
    {
        await WriteAndConfirmAsync(0x2B, QcyCommands.SetN65KeyFunction(gesture, function), cancellationToken);
        if (State.KeyFunctions.GetValueOrDefault(gesture) != function)
        {
            throw new InvalidOperationException(
                $"The earbuds kept gesture 0x{gesture:X2} on function 0x{State.KeyFunctions.GetValueOrDefault(gesture):X2}.");
        }
    }

    public Task SetChannelBalanceAsync(byte value, CancellationToken cancellationToken = default) =>
        WriteAndConfirmAsync(0x16, QcyCommands.SetChannelBalance(value), cancellationToken);

    public Task SetSleepModeAsync(bool enabled, CancellationToken cancellationToken = default) =>
        WriteAndConfirmAsync(0x10, QcyCommands.SetSleepMode(enabled), cancellationToken);

    public Task SetLdacAsync(bool enabled, CancellationToken cancellationToken = default) =>
        _isJieli
            ? WriteBeforeRestartAsync(
                0x23,
                QcyCommands.SetN65Ldac(enabled),
                state => state with { LdacEnabled = enabled },
                cancellationToken)
            : WriteAndConfirmAsync(0x23, QcyCommands.SetLdac(enabled), cancellationToken);

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
        if (_isJieli)
        {
            await WriteAndConfirmAsync(0x22, QcyN65Equalizer.SetPreset(presetIndex), cancellationToken);
            return;
        }

        await _connection.WriteAsync(QcyUuids.Equalizer, QcyCommands.SetEqualizerPreset(presetIndex), cancellationToken);
        await QueryAsync(0x22, cancellationToken);
    }

    public async Task SetCustomEqualizerAsync(
        IReadOnlyList<double> gains,
        CancellationToken cancellationToken = default)
    {
        await WriteAndConfirmAsync(0x22, QcyCommands.BuildCustomEqualizer(gains), cancellationToken);
    }

    public Task SetN65EqualizerAsync(
        IReadOnlyList<QcyEqualizerBand> bands,
        CancellationToken cancellationToken = default) =>
        WriteAndConfirmAsync(0x22, QcyN65Equalizer.BuildCustom(bands), cancellationToken);

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

            foreach (var pending in _pendingAcknowledgements.Values)
            {
                pending.TrySetCanceled();
            }

            _pendingResponses.Clear();
            _pendingAcknowledgements.Clear();
        }

        _commandLock.Dispose();
        await _connection.DisposeAsync();
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        RequireCharacteristic(_commandUuid, canWrite: true);
        RequireCharacteristic(_notificationUuid, canNotify: true);
        await _connection.SubscribeAsync(_notificationUuid, cancellationToken);
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
        if (_isJieli)
        {
            // The N65 acknowledges a write without echoing the value, so read it back.
            if (await SendAndAwaitAsync(opcode, packet, cancellationToken, acceptAcknowledgement: true) is null ||
                await QueryAsync(opcode, cancellationToken) is null)
            {
                throw new InvalidOperationException(
                    $"The earbuds did not confirm command 0x{opcode:X2}; the change was not considered applied.");
            }

            return;
        }

        var response = await SendAndAwaitAsync(opcode, packet, cancellationToken);
        if (response is null)
        {
            response = await QueryAsync(opcode, cancellationToken);
        }

        if (response is null)
        {
            throw new InvalidOperationException(
                $"The earbuds did not confirm command 0x{opcode:X2}; the change was not considered applied.");
        }
    }

    // LDAC and game mode restart the N65 link right after the acknowledgement, so it is the only
    // confirmation available.
    private async Task WriteBeforeRestartAsync(
        byte opcode,
        byte[] packet,
        Func<QcyDeviceState, QcyDeviceState> update,
        CancellationToken cancellationToken)
    {
        if (await SendAndAwaitAsync(opcode, packet, cancellationToken, acceptAcknowledgement: true) is null)
        {
            throw new InvalidOperationException(
                $"The earbuds did not acknowledge command 0x{opcode:X2}; the change was not considered applied.");
        }

        UpdateState(update);
    }

    private async Task WriteNoiseModeAsync(QcyNoiseControlState expected, CancellationToken cancellationToken)
    {
        // The N65 acknowledges an ANC change at once but ignores it while the previous transition is still
        // running, and the new mode is only pushed about a second later.
        var packet = QcyCommands.SetNoiseMode(
            expected.Mode,
            expected.CancellationMode ?? QcyNoiseCancellationMode.Adaptive);
        for (var attempt = 0; attempt < 2 && State.NoiseControl != expected; attempt++)
        {
            var wait = _lastNoiseChange + NoiseTransition - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken);
            }

            if (await SendAndAwaitAsync(0x17, packet, cancellationToken, acceptAcknowledgement: true) is null)
            {
                throw new InvalidOperationException("The earbuds did not acknowledge the noise control change.");
            }

            _lastNoiseChange = DateTimeOffset.UtcNow;
            var deadline = _lastNoiseChange + ResponseTimeout;
            while (State.NoiseControl != expected && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(150, cancellationToken);
            }
        }

        if (State.NoiseControl != expected)
        {
            await QueryAsync(0x17, cancellationToken);
        }

        var confirmed = State.NoiseControl;
        if (confirmed != expected)
        {
            throw new InvalidOperationException(
                $"The earbuds reported {FormatNoiseState(confirmed)}, but the app requested {FormatNoiseState(expected)}.");
        }
    }

    private async Task<byte[]?> SendAndAwaitAsync(
        byte expectedOpcode,
        byte[] packet,
        CancellationToken cancellationToken,
        bool acceptAcknowledgement = false)
    {
        await _commandLock.WaitAsync(cancellationToken);
        TaskCompletionSource<byte[]> completion;
        TaskCompletionSource<byte>? acknowledgement = null;
        byte sequence = 0;
        try
        {
            completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_stateLock)
            {
                _pendingResponses[expectedOpcode] = completion;
                if (_isJieli)
                {
                    sequence = _sequence++;
                    acknowledgement = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pendingAcknowledgements[sequence] = acknowledgement;
                }
            }

            var data = _isJieli ? QcyJieliFrame.Wrap(sequence, packet) : packet;
            Trace($"tx {Convert.ToHexString(data)}");
            await _connection.WriteAsync(_commandUuid, data, cancellationToken);

            try
            {
                if (acknowledgement is null)
                {
                    return await completion.Task.WaitAsync(ResponseTimeout, cancellationToken);
                }

                var first = await Task.WhenAny(completion.Task, acknowledgement.Task)
                    .WaitAsync(ResponseTimeout, cancellationToken);
                if (first == completion.Task)
                {
                    return await completion.Task;
                }

                var status = await acknowledgement.Task;
                if (status != 0)
                {
                    throw new InvalidOperationException(
                        $"The earbuds rejected command 0x{expectedOpcode:X2} (status 0x{status:X2}).");
                }

                if (acceptAcknowledgement)
                {
                    return completion.Task.IsCompletedSuccessfully ? completion.Task.Result : [];
                }

                return await completion.Task.WaitAsync(PushTimeout, cancellationToken);
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
                if (acknowledgement is not null)
                {
                    _pendingAcknowledgements.Remove(sequence);
                }
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

        if (eventArgs.CharacteristicUuid != _notificationUuid)
        {
            return;
        }

        Trace($"rx {Convert.ToHexString(eventArgs.Value)}");
        if (!_isJieli)
        {
            foreach (var command in QcyPacket.Parse(eventArgs.Value))
            {
                DispatchCommand(command);
            }

            return;
        }

        foreach (var message in QcyJieliFrame.Parse(eventArgs.Value))
        {
            foreach (var command in message.Commands)
            {
                DispatchCommand(command);
            }

            if (message.IsResponse)
            {
                TaskCompletionSource<byte>? acknowledgement;
                lock (_stateLock)
                {
                    _pendingAcknowledgements.TryGetValue(message.Sequence, out acknowledgement);
                }

                acknowledgement?.TrySetResult(message.Status ?? 0);
            }
        }
    }

    private void DispatchCommand(QcyCommand command)
    {
        if (_isJieli && N70OnlyOpcodes.Contains(command.Opcode))
        {
            return;
        }

        ApplyCommand(command);
        TaskCompletionSource<byte[]>? completion;
        lock (_stateLock)
        {
            _pendingResponses.TryGetValue(command.Opcode, out completion);
        }

        completion?.TrySetResult(command.Parameters);
    }

    private void Connection_Disconnected(object? sender, EventArgs eventArgs)
    {
        UpdateState(state => state with { IsConnected = false });
        lock (_stateLock)
        {
            foreach (var pending in _pendingResponses.Values)
            {
                pending.TrySetException(new InvalidOperationException("The earbuds disconnected during the command."));
            }

            foreach (var pending in _pendingAcknowledgements.Values)
            {
                pending.TrySetException(new InvalidOperationException("The earbuds disconnected during the command."));
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
            case 0x4A when parameters.Length >= 1:
                UpdateState(state => state with { GameModeEnabled = parameters[0] == 0x01 });
                break;
            case 0x27 when parameters.Length >= 1:
                UpdateState(state => state with { DynamicEqualizerEnabled = parameters[0] == 0x01 });
                break;
            case 0x16 when parameters.Length >= 1:
                UpdateState(state => state with { ChannelBalance = Math.Min(parameters[0], (byte)100) });
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
                        Equalizer = equalizer,
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
