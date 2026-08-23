using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace A2dpRemote;

[Service(
    Name = "dev.a2dpremote.SinkWatchService",
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public class SinkWatchService : Service
{
    public const string ActionPause = "dev.a2dpremote.action.PAUSE";
    public const string ActionResume = "dev.a2dpremote.action.RESUME";

    public static volatile string? LatestStatus;
    public static volatile bool SharedPaused;

    private const int NotificationId = 1001;
    private const string ChannelId = "c4p_channel";
    private const string LegacyChannelId = "a2dp_remote_channel";

    private A2dpConnector? _connector;
    private BluetoothDevice? _pcDevice;
    private System.Threading.Timer? _retryTimer;
    private volatile bool _userPaused;
    private bool _pausedLinkAlive;
    private int _backoffSeconds;
    private string? _pcMac;
    private readonly object _gate = new();

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        EnsureChannel();
        StartForeground(NotificationId, BuildNotification("Starting..."), ForegroundService.TypeConnectedDevice);

        if (intent?.Action == ActionPause)
        {
            EnsureInitialized("pause");
            PauseForward();
            return StartCommandResult.Sticky;
        }

        if (intent?.Action == ActionResume)
        {
            EnsureInitialized("resume");
            ResumeForward();
            return StartCommandResult.Sticky;
        }

        Initialize();
        return StartCommandResult.Sticky;
    }

    private void EnsureInitialized(string reason)
    {
        if (_pcDevice is not null && _connector is not null)
            return;

        AppendLog($"Lazy init ({reason}): service state was missing.");
        Initialize();
    }

    private void Initialize()
    {
        var connector = _connector;
        if (connector is null)
        {
            _pcMac = Prefs.Get(this, "pc_mac", null);
            if (string.IsNullOrEmpty(_pcMac))
            {
                UpdateNotification("No PC associated. Open app to associate.");
                AppendLog("Init stopped: no PC associated.");
                return;
            }

            var adapter = BluetoothAdapter.DefaultAdapter;
            if (adapter is null)
            {
                UpdateNotification("Bluetooth unavailable.");
                AppendLog("Init stopped: Bluetooth unavailable.");
                return;
            }

            try
            {
                _pcDevice = adapter.GetRemoteDevice(_pcMac);
            }
            catch
            {
                UpdateNotification("Invalid stored PC address.");
                AppendLog($"Init stopped: invalid stored PC address ({_pcMac}).");
                return;
            }

            connector = new A2dpConnector(this);
            connector.LogMessage += msg => AppendLog(msg);
            connector.ConnectionStateChanged += OnConnectionStateChanged;
            _connector = connector;
        }
        else if (connector.HasProxy)
        {
            return;
        }

        connector.AcquireProxy();

        lock (_gate)
        {
            _backoffSeconds = 0;
        }
        ScheduleRetry(3);
    }

    private void OnConnectionStateChanged(BluetoothDevice device, int state)
    {
        if (_pcDevice is null || device.Address != _pcDevice.Address)
            return;

        switch (state)
        {
            case (int)ProfileState.Connected:
                UpdateNotification($"Connected - {_pcDevice.Name ?? _pcMac}");
                lock (_gate)
                {
                    _backoffSeconds = 0;
                }
                StopRetryTimer();
                break;

            case (int)ProfileState.Disconnected:
                UpdateNotification("Disconnected.");
                bool reconnect;
                lock (_gate)
                {
                    _pausedLinkAlive = false;
                    reconnect = !_userPaused;
                }
                if (reconnect)
                    ScheduleRetry(10);
                break;
        }
    }

    private void ScheduleRetry(int seconds)
    {
        lock (_gate)
        {
            DestroyRetryTimerLocked();
            _retryTimer = new Timer(_ => AttemptConnect(), null, seconds * 1000, Timeout.Infinite);
        }
    }

    private void StopRetryTimer()
    {
        lock (_gate)
        {
            DestroyRetryTimerLocked();
        }
    }

    private void DestroyRetryTimerLocked()
    {
        _retryTimer?.Dispose();
        _retryTimer = null;
    }

    private void AttemptConnect()
    {
        A2dpConnector? connector;
        BluetoothDevice? device;
        lock (_gate)
        {
            connector = _connector;
            device = _pcDevice;
        }

        if (connector is null || device is null || _userPaused)
            return;

        if (connector.IsConnected(device))
        {
            connector.TrySetActiveDevice(device);
            UpdateNotification($"Connected - {device.Name ?? _pcMac}");
            return;
        }

        connector.InvokeHidden("connect", device);

        int backoff;
        lock (_gate)
        {
            _backoffSeconds = Math.Min(_backoffSeconds == 0 ? 10 : _backoffSeconds * 2, 60);
            backoff = _backoffSeconds;
        }

        ScheduleRetry(backoff);
        UpdateNotification($"Connecting (retry {backoff}s)...");
    }

    private void PauseForward()
    {
        _userPaused = true;
        StopRetryTimer();

        var connector = _connector;
        var device = _pcDevice;

        if (connector is null || device is null)
        {
            UpdateNotification("No PC associated. Open app to associate.");
            AppendLog("Pause skipped: not initialized.");
            return;
        }

        if (!connector.IsConnected(device))
        {
            lock (_gate)
            {
                _pausedLinkAlive = false;
            }
            UpdateNotification("Paused - already detached.");
            AppendLog("Pause: nothing was routed to the PC.");
            return;
        }

        if (connector.TrySetActiveDevice(null))
        {
            var activeAfter = connector.GetActiveDevice();
            bool routedAway = activeAfter is null || activeAfter.Address != device.Address;

            if (routedAway)
            {
                lock (_gate)
                {
                    _pausedLinkAlive = true;
                }
                UpdateNotification("Paused - audio on phone (link held).");
                AppendLog("Pause: setActiveDevice(null) succeeded, link held.");
                return;
            }

            AppendLog("setActiveDevice(null) did not reroute - falling back.");
        }
        else
        {
            AppendLog("setActiveDevice unavailable - falling back.");
        }

        connector.InvokeHidden("disconnect", device);
        lock (_gate)
        {
            _pausedLinkAlive = false;
        }
        UpdateNotification("Paused - audio on phone.");
        AppendLog("Pause via profile disconnect (fallback path).");
    }

    private void ResumeForward()
    {
        _userPaused = false;
        lock (_gate)
        {
            _backoffSeconds = 0;
        }
        AppendLog("Resume requested.");

        var connector = _connector;
        var device = _pcDevice;

        if (connector is null || device is null)
        {
            UpdateNotification("No PC associated. Open app to associate.");
            AppendLog("Resume skipped: not initialized.");
            return;
        }

        bool linkHeld;
        lock (_gate)
        {
            linkHeld = _pausedLinkAlive;
        }

        if (linkHeld)
        {
            if (connector.TrySetActiveDevice(device))
            {
                var active = connector.GetActiveDevice();
                bool verified = active is not null && active.Address == device.Address;

                UpdateNotification(verified
                    ? $"Connected - {device.Name ?? _pcMac}"
                    : $"Resuming - routing pending ({device.Name ?? _pcMac})...");
                AppendLog(verified
                    ? "Resume: instant routing flip verified."
                    : "Resume: routing flip requested (state pending).");
                return;
            }

            AppendLog("setActiveDevice lost - falling back to reconnect.");
            lock (_gate)
            {
                _pausedLinkAlive = false;
            }
            ScheduleRetry(3);
            return;
        }

        if (connector.IsConnected(device))
        {
            if (connector.TrySetActiveDevice(device))
            {
                var active = connector.GetActiveDevice();
                bool verified = active is not null && active.Address == device.Address;

                UpdateNotification(verified
                    ? $"Connected - {device.Name ?? _pcMac}"
                    : $"Resuming - routing pending ({device.Name ?? _pcMac})...");
                AppendLog(verified
                    ? "Resume: reactivated existing session."
                    : "Resume: activation requested (state pending).");
                return;
            }
        }

        if (!connector.HasProxy)
        {
            AppendLog("Resume: A2DP proxy pending, retrying shortly.");
            ScheduleRetry(3);
            UpdateNotification("Resuming - connecting...");
            return;
        }

        if (!connector.InvokeHidden("connect", device))
        {
            ScheduleRetry(10);
            UpdateNotification("Resuming: retrying in 10s...");
        }
    }

    private static void EnsureChannel()
    {
        var manager = (NotificationManager?)global::Android.App.Application.Context.GetSystemService(NotificationService);
        if (manager is null)
            return;

        if (manager.GetNotificationChannel(ChannelId) is null)
            manager.CreateNotificationChannel(new NotificationChannel(ChannelId, "C4P", NotificationImportance.Low));

        if (LegacyChannelId != ChannelId && manager.GetNotificationChannel(LegacyChannelId) is not null)
            manager.DeleteNotificationChannel(LegacyChannelId);
    }

    private Notification BuildNotification(string text)
    {
        var pauseIntent = new Intent(this, typeof(SinkWatchService));
        pauseIntent.SetAction(ActionPause);
        var resumeIntent = new Intent(this, typeof(SinkWatchService));
        resumeIntent.SetAction(ActionResume);

        PendingIntentFlags immutableFlags = PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable;
        var playIcon = Android.Graphics.Drawables.Icon.CreateWithResource(PackageName, Android.Resource.Drawable.IcMediaPlay);
        var pauseIcon = Android.Graphics.Drawables.Icon.CreateWithResource(PackageName, Android.Resource.Drawable.IcMediaPause);

        var builder = new Notification.Builder(this, ChannelId)
            .SetContentTitle("C4P")
            .SetContentText(text)
            .SetSmallIcon(Android.Resource.Drawable.IcMediaPlay)
            .SetOngoing(true)
            .SetContentIntent(PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)), immutableFlags))
            .AddAction(new Notification.Action.Builder(pauseIcon, "Pause", PendingIntent.GetService(this, 1, pauseIntent, immutableFlags)).Build())
            .AddAction(new Notification.Action.Builder(playIcon, "Resume", PendingIntent.GetService(this, 2, resumeIntent, immutableFlags)).Build());

        return builder.Build()!;
    }

    private void UpdateNotification(string text)
    {
        LatestStatus = text;
        SharedPaused = _userPaused;

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        manager?.Notify(NotificationId, BuildNotification(text));
    }

    private void AppendLog(string message)
    {
        Prefs.AppendLog(this, $"{DateTime.Now:HH:mm:ss} {message}");
    }

    public override void OnDestroy()
    {
        StopRetryTimer();
        _connector?.Release();
        _connector = null;
        LatestStatus = null;
        SharedPaused = false;
        base.OnDestroy();
    }
}
