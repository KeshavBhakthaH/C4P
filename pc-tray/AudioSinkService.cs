using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Media.Audio;

namespace A2dpSink;

internal enum SinkState
{
    Disconnected,
    Connecting,
    Connected
}

internal sealed record SinkStatus(SinkState State, bool Paused, string DeviceName, bool DeviceSeen)
{
    public string ToProtocolText()
    {
        string baseText = State switch
        {
            SinkState.Connecting => "CONNECTING",
            SinkState.Connected => $"CONNECTED ({DeviceName})",
            _ when DeviceSeen => "DISCONNECTED",
            _ => "NO_DEVICE_SEEN"
        };

        return Paused ? baseText + " [PAUSED]" : baseText;
    }
}

internal readonly record struct OpResult(bool Success, string Detail)
{
    public static OpResult Ok(string detail) => new(true, detail);
    public static OpResult Fail(string detail) => new(false, detail);
}

internal sealed class AudioSinkService : IAsyncDisposable
{
    private const string IsConnectedProperty = "System.Devices.Aep.IsConnected";

    private static readonly TimeSpan RecoveryCycleDelay = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan PipelineDeadline = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan HealPeriod = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private int _generation;
    private int _healAttemptCount;
    private int _consecutiveHealFailures;
    private DeviceWatcher? _nudgeWatcher;

    private DeviceWatcher? _watcher;
    private AudioPlaybackConnection? _connection;

    private string _deviceId = string.Empty;
    private string _deviceName = string.Empty;
    private SinkState _state = SinkState.Disconnected;
    private bool _forwardingPaused;
    private bool _deviceWasPreConnected;
    private bool _suppressAutoConnect;

    public event Action<SinkStatus>? StatusChanged;
    public event Action<string>? MessageRaised;

    public SinkStatus CurrentStatus => new(_state, _forwardingPaused, _deviceName, _deviceId.Length > 0);

    public void Start()
    {
        if (_watcher is not null)
            return;

        string selector = AudioPlaybackConnection.GetDeviceSelector();
        var additionalProperties = new List<string> { IsConnectedProperty };
        _watcher = DeviceInformation.CreateWatcher(selector, additionalProperties);
        _watcher.Added += Watcher_Added;
        _watcher.Removed += Watcher_Removed;
        _watcher.Start();

        _ = HealingLoopAsync();
    }

    private async Task HealingLoopAsync()
    {
        while (true)
        {
            await Task.Delay(HealPeriod);

            bool deviceAwaitingLink;

            await _stateGate.WaitAsync();
            try
            {
                deviceAwaitingLink = _state == SinkState.Disconnected
                                     && !_forwardingPaused
                                     && !_suppressAutoConnect
                                     && _deviceId.Length > 0;
            }
            finally
            {
                _stateGate.Release();
            }

            if (!deviceAwaitingLink)
                continue;

            _healAttemptCount++;
            bool useRecoveryCycle = _healAttemptCount % 3 == 0;

            try
            {
                OpResult result = await ConnectAsyncCore(forceRecoveryCycle: useRecoveryCycle, CancellationToken.None);

                if (result.Success)
                {
                    _consecutiveHealFailures = 0;
                    if (result.Detail == "CONNECTED")
                        MessageRaised?.Invoke($"Re-established link with {_deviceName}.");
                }
                else
                {
                    _consecutiveHealFailures++;

                    if (_consecutiveHealFailures >= 3 && _consecutiveHealFailures % 3 == 0)
                    {
                        bool toggled = await TryRadioToggleAsync();
                        if (!toggled)
                            MessageRaised?.Invoke("Could not restart Bluetooth automatically. Toggle it once on the PC, then on the phone.");
                    }
                }
            }
            catch
            {
            }
        }
    }

    public Task<OpResult> ConnectAsync(CancellationToken ct = default)
    {
        return ConnectAsyncCore(forceRecoveryCycle: false, ct);
    }

    private async Task<OpResult> ConnectAsyncCore(bool forceRecoveryCycle, CancellationToken ct)
    {
        bool recoveryCycle;

        await _stateGate.WaitAsync(ct);
        try
        {
            if (_state == SinkState.Connected)
                return OpResult.Ok("ALREADY_CONNECTED");

            if (_state == SinkState.Connecting)
                return OpResult.Fail("BUSY");

            if (_deviceId.Length == 0)
                return OpResult.Fail("NO_DEVICE_FOUND");

            _suppressAutoConnect = false;
            _state = SinkState.Connecting;
            RaiseStatus();

            recoveryCycle = forceRecoveryCycle || _deviceWasPreConnected;
        }
        finally
        {
            _stateGate.Release();
        }

        return await RunPipelineAsync(recoveryCycle, resumeRetries: 0);
    }

    public async Task<OpResult> DisconnectAsync(CancellationToken ct = default)
    {
        AudioPlaybackConnection? toClose;

        await _stateGate.WaitAsync(ct);
        try
        {
            if (_state == SinkState.Disconnected && _connection is null)
                return OpResult.Ok("ALREADY_DISCONNECTED");

            toClose = DetachConnectionLocked();


            _suppressAutoConnect = true;
            _state = SinkState.Disconnected;
            RaiseStatus();
        }
        finally
        {
            _stateGate.Release();
        }

        DisposeQuiet(toClose);
        return OpResult.Ok("DISCONNECTED");
    }

    public async Task<OpResult> PauseForwardingAsync()
    {
        AudioPlaybackConnection? toClose;

        await _stateGate.WaitAsync();
        try
        {
            if (_forwardingPaused && _state == SinkState.Disconnected && _connection is null)
                return OpResult.Ok("ALREADY_PAUSED");

            toClose = DetachConnectionLocked();
            _generation++;
            _forwardingPaused = true;
            _state = SinkState.Disconnected;
            RaiseStatus();
        }
        finally
        {
            _stateGate.Release();
        }

        DisposeQuiet(toClose);
        MessageRaised?.Invoke("Forwarding paused. Audio returns to the phone speaker.");
        return OpResult.Ok("PAUSED");
    }

    public async Task<OpResult> ResumeForwardingAsync()
    {
        bool launch;

        await _stateGate.WaitAsync();
        try
        {
            if (!_forwardingPaused)
                return _state == SinkState.Connected ? OpResult.Ok("ALREADY_CONNECTED") : OpResult.Fail("NOT_PAUSED");

            _forwardingPaused = false;
            _suppressAutoConnect = false;
            RaiseStatus();

            if (_deviceId.Length == 0)
                return OpResult.Fail("NO_DEVICE_FOUND");

            _state = SinkState.Connecting;
            RaiseStatus();
            launch = true;
        }
        finally
        {
            _stateGate.Release();
        }

        if (!launch)
            return OpResult.Fail("NOT_PAUSED");

        return await RunPipelineAsync(recoveryCycle: false, resumeRetries: 1);
    }

    private async Task<OpResult> RunPipelineAsync(bool recoveryCycle, int resumeRetries)
    {
        int generation = ++_generation;

        Task<OpResult> worker = Task.Run(() => PipelineWorkerAsync(generation, recoveryCycle, resumeRetries));

        Task completed = await Task.WhenAny(worker, Task.Delay(PipelineDeadline));
        if (completed != worker)
        {
            _generation++;
            await SetDisconnectedAsync();
            MessageRaised?.Invoke($"Bluetooth did not respond within {(int)PipelineDeadline.TotalSeconds}s. Toggle Bluetooth on the phone once, then retry.");
            return OpResult.Fail("OPEN_FAILED_TIMEOUT");
        }

        return await worker;
    }

    private async Task<OpResult> PipelineWorkerAsync(int generation, bool recoveryCycle, int resumeRetries)
    {
        StartNudgeWatcher();
        try
        {
            return await PipelineWorkerInnerAsync(generation, recoveryCycle, resumeRetries);
        }
        finally
        {
            StopNudgeWatcher();
        }
    }

    private async Task<OpResult> PipelineWorkerInnerAsync(int generation, bool recoveryCycle, int resumeRetries)
    {
        string? lastError = null;

        try
        {
            AudioPlaybackConnection? connection = null;

            if (recoveryCycle)
            {
                var first = await OpenFreshConnectionAsync();
                connection = first.Connection;
                lastError = first.Error ?? lastError;

                if (connection is not null)
                {
                    await Task.Delay(RecoveryCycleDelay);
                    if (generation != Volatile.Read(ref _generation))
                        return OpResult.Fail("SUPERSEDED");

                    DisposeQuiet(connection);
                    connection = null;
                }

                if (generation != Volatile.Read(ref _generation))
                    return OpResult.Fail("SUPERSEDED");

                var second = await OpenFreshConnectionAsync();
                connection = second.Connection;
                lastError = second.Error ?? lastError;

                if (connection is not null)
                    MessageRaised?.Invoke($"Recovered stale link with {_deviceName}. If audio still does not flow, toggle Bluetooth on the phone once.");
            }
            else
            {
                int attempts = 1 + Math.Max(0, resumeRetries);

                for (int attempt = 0; attempt < attempts && connection is null; attempt++)
                {
                    if (attempt > 0)
                        await Task.Delay(1000);

                    if (generation != Volatile.Read(ref _generation))
                        return OpResult.Fail("SUPERSEDED");

                    var opened = await OpenFreshConnectionAsync();
                    connection = opened.Connection;
                    lastError = opened.Error ?? lastError;
                }
            }

            if (connection is null)
            {
                await SetDisconnectedAsync();
                return OpResult.Fail(lastError ?? "OPEN_FAILED");
            }

            await _stateGate.WaitAsync();
            try
            {
                if (generation != Volatile.Read(ref _generation))
                    return OpResult.Fail("SUPERSEDED");

                _connection = connection;
                _state = SinkState.Connected;
                _deviceWasPreConnected = false;
                RaiseStatus();
            }
            finally
            {
                _stateGate.Release();
            }

            return OpResult.Ok("CONNECTED");
        }
        catch
        {
            await SetDisconnectedAsync();
            return OpResult.Fail(lastError ?? "OPEN_FAILED");
        }
    }

    private async Task<(AudioPlaybackConnection? Connection, string? Error)> OpenFreshConnectionAsync()
    {
        var connection = AudioPlaybackConnection.TryCreateFromId(_deviceId);
        if (connection is null)
            return (null, "OPEN_FAILED_UNSUPPORTED_DEVICE");

        connection.StateChanged += Connection_StateChanged;
        try
        {
            using var timeoutCts = new CancellationTokenSource(OpenTimeout);

            await connection.StartAsync().AsTask(timeoutCts.Token);
            AudioPlaybackConnectionOpenResult result = await connection.OpenAsync().AsTask(timeoutCts.Token);

            if (result.Status == AudioPlaybackConnectionOpenResultStatus.Success)
            {
                for (int i = 0; i < 15 && connection.State != AudioPlaybackConnectionState.Opened; i++)
                    await Task.Delay(100);

                if (connection.State == AudioPlaybackConnectionState.Opened)
                    return (connection, null);
            }

            uint hr = 0;
            if (result.ExtendedError is System.Exception extended)
                hr = (uint)extended.HResult;

            DisposeQuiet(connection);
            return (null, $"OPEN_FAILED_{result.Status.ToString().ToUpperInvariant()}_0x{hr:X8}");
        }
        catch (OperationCanceledException)
        {
            DisposeQuiet(connection);
            return (null, "OPEN_FAILED_TIMEOUT");
        }
        catch (Exception ex)
        {
            string hresult = $"0x{(uint)ex.HResult:X8}";
            DisposeQuiet(connection);
            return (null, $"OPEN_FAILED_{ex.GetType().Name.ToUpperInvariant()}_{hresult}");
        }
    }

    private void StartNudgeWatcher()
    {
        try
        {
            string selector = BluetoothDevice.GetDeviceSelector();
            _nudgeWatcher = DeviceInformation.CreateWatcher(selector);
            _nudgeWatcher.Start();
        }
        catch
        {
            _nudgeWatcher = null;
        }
    }

    private void StopNudgeWatcher()
    {
        var watcher = _nudgeWatcher;
        _nudgeWatcher = null;

        if (watcher is null)
            return;

        try
        {
            watcher.Stop();
        }
        catch
        {
        }
    }

    private async Task<bool> TryRadioToggleAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            Radio? bluetooth = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);

            if (bluetooth is null || bluetooth.State != RadioState.On)
                return false;

            MessageRaised?.Invoke("Restarting the Bluetooth radio to recover discoverability...");

            await bluetooth.SetStateAsync(RadioState.Off);
            await Task.Delay(3000);
            await bluetooth.SetStateAsync(RadioState.On);
            await Task.Delay(6000);

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        AudioPlaybackConnection? toClose;

        await _stateGate.WaitAsync();
        try
        {
            toClose = DetachConnectionLocked();
            _state = SinkState.Disconnected;


            if (_watcher is not null)
            {
                _watcher.Added -= Watcher_Added;
                _watcher.Removed -= Watcher_Removed;
                _watcher.Stop();
                _watcher = null;
            }
        }
        finally
        {
            _stateGate.Release();
        }

        DisposeQuiet(toClose);
    }

    private AudioPlaybackConnection? DetachConnectionLocked()
    {
        var connection = _connection;
        _connection = null;
        return connection;
    }

    private async Task SetDisconnectedAsync()
    {
        AudioPlaybackConnection? toClose;

        await _stateGate.WaitAsync();
        try
        {
            toClose = DetachConnectionLocked();
            _state = SinkState.Disconnected;
            RaiseStatus();
        }
        finally
        {
            _stateGate.Release();
        }

        DisposeQuiet(toClose);
    }

    private void DisposeQuiet(AudioPlaybackConnection? connection)
    {
        if (connection is null)
            return;

        connection.StateChanged -= Connection_StateChanged;
        try { connection.Dispose(); } catch { }
    }

    private async void Watcher_Added(DeviceWatcher sender, DeviceInformation deviceInfo)
    {
        bool supported;
        try
        {
            using var probe = AudioPlaybackConnection.TryCreateFromId(deviceInfo.Id);
            supported = probe is not null;
        }
        catch
        {
            return;
        }

        if (!supported)
            return;

        bool shouldAutoConnect;

        await _stateGate.WaitAsync();
        try
        {
            if (_state != SinkState.Disconnected || _deviceId.Length > 0)
                return;

            _deviceId = deviceInfo.Id;
            _deviceName = deviceInfo.Name;
            _deviceWasPreConnected = deviceInfo.Properties.TryGetValue(IsConnectedProperty, out object? value)
                                     && value is bool connected && connected;
            shouldAutoConnect = !_suppressAutoConnect;
            RaiseStatus();
        }
        finally
        {
            _stateGate.Release();
        }

        if (shouldAutoConnect)
            await SafeAutoConnectAsync();
    }

    private async Task SafeAutoConnectAsync()
    {
        try
        {
            await ConnectAsync();
        }
        catch
        {
        }
    }

    private async void Watcher_Removed(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        try
        {
            await _stateGate.WaitAsync();
            try
            {
                if (_state != SinkState.Disconnected || update.Id != _deviceId)
                    return;

                _deviceId = string.Empty;
                _deviceName = string.Empty;
                _deviceWasPreConnected = false;
                RaiseStatus();
            }
            finally
            {
                _stateGate.Release();
            }
        }
        catch
        {
        }
    }

    private async void Connection_StateChanged(AudioPlaybackConnection sender, object args)
    {
        if (sender.State != AudioPlaybackConnectionState.Closed)
            return;

        try
        {
            await _stateGate.WaitAsync();
            AudioPlaybackConnection? toClose = null;
            try
            {
                if (!ReferenceEquals(_connection, sender) || _state != SinkState.Connected)
                    return;

                toClose = DetachConnectionLocked();
                _state = SinkState.Disconnected;
                RaiseStatus();
            }
            finally
            {
                _stateGate.Release();
            }

            DisposeQuiet(toClose);
            MessageRaised?.Invoke($"{_deviceName} dropped the audio link.");
        }
        catch
        {
        }
    }

    private void RaiseStatus() => StatusChanged?.Invoke(CurrentStatus);
}
