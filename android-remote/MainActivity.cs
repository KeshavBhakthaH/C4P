using Android;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace A2dpRemote;

[Activity(
    Label = "C4P",
    MainLauncher = true,
    Exported = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public class MainActivity : Activity
{
    private const int PermissionRequestCode = 7;
    private const int ScanRequestCode = 31;

    private TextView _statusView = null!;
    private Button _pauseButton = null!;
    private Button _scanButton = null!;
    private Button _setupToggle = null!;
    private LinearLayout _setupSection = null!;
    private Button _logToggle = null!;
    private LinearLayout _logSection = null!;
    private TextView _logView = null!;
    private EditText _ipBox = null!;
    private EditText _keyBox = null!;

    private readonly Handler _uiHandler = new(Looper.MainLooper!);
    private Java.Lang.Runnable? _tick;
    private bool _active;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(BuildLayout());
        EnsurePermissionsAndMaybeStartService();
    }

    protected override void OnResume()
    {
        base.OnResume();
        _active = true;
        _tick ??= new Java.Lang.Runnable(Tick);
        Tick();
    }

    protected override void OnPause()
    {
        _active = false;
        if (_tick != null)
            _uiHandler.RemoveCallbacks(_tick);
        base.OnPause();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (requestCode != ScanRequestCode || data is null)
            return;

        string? payload = data.GetStringExtra(ScanPairingActivity.ExtraPayload);

        if (resultCode != Result.Ok || payload is null)
            return;

        _ = ApplyPairingPayloadAsync(payload);
    }

    private async Task ApplyPairingPayloadAsync(string? payload)
    {
        if (!PcPairing.TryParse(payload, out string[] ips, out int port, out string key, out string? mac))
        {
            Toast.MakeText(this, "That QR was not a C4P pairing code.", ToastLength.Long)?.Show();
            Log("Scan: QR did not contain a valid C4P payload.");
            return;
        }

        Prefs.Set(this, "pc_key", key);
        if (_keyBox.Text?.Trim() != key)
            _keyBox.Text = key;

        Log($"Scanned pairing QR: {ips.Length} candidate IP(s).");

        foreach (string ip in ips)
        {
            string reply = await PcClient.SendAsync(ip, port, "STATUS", key);
            Log($"Pair test {ip}: {reply}");

            if (reply.StartsWith("STATUS", StringComparison.Ordinal) || reply.StartsWith("OK", StringComparison.Ordinal))
            {
                Prefs.Set(this, "pc_ip", ip);
                _ipBox.Text = ip;

                if (!string.IsNullOrEmpty(mac))
                    AssociateAndRestart(mac);
                else
                    Log("QR had no Bluetooth MAC - keeping the existing association.");

                Toast.MakeText(this, $"Paired with PC at {ip}", ToastLength.Long)?.Show();
                return;
            }
        }

        Toast.MakeText(this,
            $"Key saved, but no PC answered at {string.Join(", ", ips)}. Check Wi-Fi and firewall.",
            ToastLength.Long)?.Show();
    }

    private View BuildLayout()
    {
        var scroll = new ScrollView(this);
        var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
        root.SetPadding(56, 56, 56, 40);
        scroll.AddView(root);

        var title = new TextView(this) { Text = "C4P", TextSize = 30f };
        title.SetTypeface(null!, TypefaceStyle.Bold);
        root.AddView(title);

        _statusView = new TextView(this) { TextSize = 17f, Text = "Starting..." };
        root.AddView(_statusView, Pad(0, 20, 0, 0));

        _pauseButton = new Button(this);
        _pauseButton.Click += (_, _) =>
        {
            bool paused = SinkWatchService.SharedPaused;
            SendServiceAction(paused ? SinkWatchService.ActionResume : SinkWatchService.ActionPause);
            Log(paused ? "UI: resume requested." : "UI: pause requested.");
        };
        root.AddView(_pauseButton, Pad(0, 12, 0, 0));

        _setupToggle = MakeSectionToggle("Setup");
        _setupToggle.Click += (_, _) => ToggleSection(_setupToggle, _setupSection, "Setup");
        root.AddView(_setupToggle, Pad(0, 28, 0, 0));

        _setupSection = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };

        _scanButton = new Button(this) { Text = "Scan pairing QR" };
        _scanButton.Click += (_, _) =>
        {
            StartActivityForResult(typeof(ScanPairingActivity), ScanRequestCode);
        };
        _setupSection.AddView(_scanButton);

        _ipBox = new EditText(this)
        {
            Hint = "PC IP address (e.g. 192.168.1.50)",
            Text = Prefs.Get(this, "pc_ip", string.Empty)
        };
        _ipBox.FocusChange += (_, e) =>
        {
            if (!e.HasFocus)
                Prefs.Set(this, "pc_ip", _ipBox.Text.Trim());
        };
        _setupSection.AddView(_ipBox, Pad(0, 8, 0, 0));

        _keyBox = new EditText(this)
        {
            Hint = "Pairing key (PC tray menu: Copy pairing key)",
            Text = Prefs.Get(this, "pc_key", string.Empty),
            InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword
        };
        _keyBox.FocusChange += (_, e) =>
        {
            if (!e.HasFocus)
                Prefs.Set(this, "pc_key", _keyBox.Text.Trim());
        };
        _setupSection.AddView(_keyBox, Pad(0, 8, 0, 0));

        var keyToggle = new Button(this) { Text = "Show key" };
        keyToggle.Click += (_, _) =>
        {
            const int TextVariationMask = 0x0000f000;

            bool hidden = ((int)_keyBox.InputType & TextVariationMask)
                == (int)Android.Text.InputTypes.TextVariationPassword;

            _keyBox.InputType = hidden
                ? Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationNormal
                : Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword;

            keyToggle.Text = hidden ? "Hide key" : "Show key";
        };
        _setupSection.AddView(keyToggle, Pad(0, 4, 0, 0));

        var findButton = new Button(this) { Text = "Find PC automatically" };
        findButton.Click += async (_, _) =>
        {
            findButton.Enabled = false;
            Toast.MakeText(this, "Searching for PC...", ToastLength.Short)?.Show();

            List<(string Ip, string Name)> found = await PcDiscovery.DiscoverAsync(Prefs.Get(this, "pc_key", string.Empty));

            if (found.Count > 0)
            {
                (string ip, string name) = found[0];
                _ipBox.Text = ip;
                Prefs.Set(this, "pc_ip", ip);

                string extra = found.Count > 1 ? $" (+{found.Count - 1} more)" : string.Empty;
                Toast.MakeText(this, $"Found PC '{name}' at {ip}{extra}", ToastLength.Long)?.Show();
                Log($"Discovery: '{name}' at {ip}{extra}");
            }
            else
            {
                Toast.MakeText(this, "No PC answered. Type the IP manually.", ToastLength.Long)?.Show();
                Log("Discovery: no PC answered.");
            }

            findButton.Enabled = true;
        };
        _setupSection.AddView(findButton, Pad(0, 8, 0, 0));

        var refreshButton = new Button(this) { Text = "Test PC link" };
        refreshButton.Click += async (_, _) =>
        {
            Prefs.Set(this, "pc_ip", _ipBox.Text.Trim());
            Prefs.Set(this, "pc_key", _keyBox.Text.Trim());
            string reply = await PcClient.SendAsync(_ipBox.Text.Trim(), 8080, "STATUS", _keyBox.Text.Trim());
            Log($"PC STATUS: {reply}");
            Toast.MakeText(this, $"PC: {reply}", ToastLength.Long)?.Show();
        };
        _setupSection.AddView(refreshButton, Pad(0, 8, 0, 0));

        root.AddView(_setupSection);

        _logToggle = MakeSectionToggle("Log");
        _logToggle.Click += (_, _) =>
        {
            bool opening = _logSection.Visibility != ViewStates.Visible;
            ToggleSection(_logToggle, _logSection, "Log");
            if (opening)
                _logView.Text = Prefs.Get(this, "log_tail", "(empty)");
        };
        root.AddView(_logToggle, Pad(0, 8, 0, 0));

        _logSection = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
        _logView = new TextView(this) { TextSize = 12f, Text = "(empty)" };
        _logSection.AddView(_logView, Pad(0, 8, 0, 0));
        root.AddView(_logSection);

        if (string.IsNullOrEmpty(Prefs.Get(this, "pc_mac", null)))
        {
            _setupSection.Visibility = ViewStates.Visible;
            _setupToggle.Text = "Hide setup";
        }

        return scroll;
    }

    private void AssociateAndRestart(string? macInput)
    {
        string mac = (macInput ?? string.Empty).Trim().ToUpperInvariant();

        if (!System.Text.RegularExpressions.Regex.IsMatch(mac, @"^([0-9A-F]{2}:){5}[0-9A-F]{2}$"))
        {
            Log("MAC format invalid. Example: 1A:2B:3C:4D:5E:6F");
            Toast.MakeText(this, "MAC format invalid.", ToastLength.Long)?.Show();
            return;
        }

        Prefs.Set(this, "pc_mac", mac);
        Log($"Associated with {mac}. Restarting sink service...");

        StopService(new Intent(this, typeof(SinkWatchService)));
        StartForegroundService(new Intent(this, typeof(SinkWatchService)));
    }

    private void Tick()
    {
        _statusView.Text = SinkWatchService.LatestStatus ?? "Service not running";
        bool paused = SinkWatchService.SharedPaused;
        _pauseButton.Text = paused ? "Resume forwarding" : "Pause forwarding";

        if (_logSection.Visibility == ViewStates.Visible)
            _logView.Text = Prefs.Get(this, "log_tail", "(empty)");

        if (_active && _tick != null)
            _uiHandler.PostDelayed(_tick, 1500);
    }

    private Button MakeSectionToggle(string name)
    {
        return new Button(this) { Text = $"Show {name.ToLowerInvariant()}" };
    }

    private static void ToggleSection(Button toggle, View section, string name)
    {
        bool showing = section.Visibility == ViewStates.Visible;
        section.Visibility = showing ? ViewStates.Gone : ViewStates.Visible;
        toggle.Text = showing
            ? $"Show {name.ToLowerInvariant()}"
            : $"Hide {name.ToLowerInvariant()}";
    }

    private static LinearLayout.LayoutParams Pad(int left, int top, int right, int bottom)
    {
        return new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            LeftMargin = left,
            TopMargin = top,
            RightMargin = right,
            BottomMargin = bottom
        };
    }

    private void EnsurePermissionsAndMaybeStartService()
    {
        bool bluetoothReady = (int)Build.VERSION.SdkInt < 31 ||
            CheckSelfPermission(Manifest.Permission.BluetoothConnect) == Permission.Granted;

        var needed = new List<string>();
        if (!bluetoothReady)
            needed.Add(Manifest.Permission.BluetoothConnect);

        if ((int)Build.VERSION.SdkInt >= 33 && CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
            needed.Add(Manifest.Permission.PostNotifications);

        if (needed.Count > 0)
            RequestPermissions(needed.ToArray(), PermissionRequestCode);

        if (bluetoothReady)
        {
            StartForegroundService(new Intent(this, typeof(SinkWatchService)));
            Log("App opened - sink service ensured.");
        }
        else
        {
            Log("Approve the Bluetooth permission to start the sink service.");
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);

        if (requestCode != PermissionRequestCode)
            return;

        bool bluetoothGranted = false;
        for (int i = 0; i < permissions.Length; i++)
        {
            bool granted = grantResults[i] == Permission.Granted;
            Log($"{permissions[i]}: {(granted ? "granted" : "denied")}");

            if (granted && permissions[i] == Manifest.Permission.BluetoothConnect)
                bluetoothGranted = true;
        }

        if (bluetoothGranted)
            StartForegroundService(new Intent(this, typeof(SinkWatchService)));
    }

    private void SendServiceAction(string action)
    {
        var intent = new Intent(this, typeof(SinkWatchService));
        intent.SetAction(action);
        StartForegroundService(intent);
    }

    private void Log(string message)
    {
        Prefs.AppendLog(this, $"{DateTime.Now:HH:mm:ss} {message}");
        if (_logSection.Visibility == ViewStates.Visible)
            _logView.Text = Prefs.Get(this, "log_tail", string.Empty);
    }
}
