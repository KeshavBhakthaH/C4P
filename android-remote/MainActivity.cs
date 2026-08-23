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

    private TextView _statusView = null!;
    private Button _pauseButton = null!;
    private Button _setupToggle = null!;
    private LinearLayout _setupSection = null!;
    private Button _logToggle = null!;
    private LinearLayout _logSection = null!;
    private TextView _logView = null!;
    private EditText _ipBox = null!;
    private EditText _macBox = null!;
    private LinearLayout _pairedList = null!;
    private TextView _pairedHint = null!;

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
        RefreshPairedDevices();
    }

    protected override void OnPause()
    {
        _active = false;
        if (_tick != null)
            _uiHandler.RemoveCallbacks(_tick);
        base.OnPause();
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

        var refreshButton = new Button(this) { Text = "Test PC link" };
        refreshButton.Click += async (_, _) =>
        {
            Prefs.Set(this, "pc_ip", _ipBox.Text.Trim());
            string reply = await PcClient.SendAsync(_ipBox.Text.Trim(), 8080, "STATUS");
            Log($"PC STATUS: {reply}");
            Toast.MakeText(this, $"PC: {reply}", ToastLength.Long)?.Show();
        };
        _setupSection.AddView(refreshButton, Pad(0, 8, 0, 0));

        var pairedHeader = new TextView(this) { Text = "Paired devices (tap to associate)", TextSize = 14f };
        _setupSection.AddView(pairedHeader, Pad(0, 16, 0, 0));

        _pairedHint = new TextView(this) { TextSize = 12f, Text = "Loading paired devices..." };
        _setupSection.AddView(_pairedHint, Pad(0, 8, 0, 0));

        _pairedList = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _setupSection.AddView(_pairedList, Pad(0, 4, 0, 0));

        var manualHeader = new TextView(this) { Text = "Or type the MAC manually", TextSize = 14f };
        _setupSection.AddView(manualHeader, Pad(0, 16, 0, 0));

        _macBox = new EditText(this)
        {
            Hint = "PC Bluetooth MAC (AA:BB:CC:DD:EE:FF)",
            Text = Prefs.Get(this, "pc_mac", string.Empty)
        };
        _setupSection.AddView(_macBox, Pad(0, 8, 0, 0));

        var saveMacButton = new Button(this) { Text = "Save MAC + restart sink service" };
        saveMacButton.Click += (_, _) => AssociateAndRestart(_macBox.Text);
        _setupSection.AddView(saveMacButton, Pad(0, 8, 0, 0));

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

    private void RefreshPairedDevices()
    {
        if (_pairedList is null || _pairedHint is null)
            return;

        _pairedList.RemoveAllViews();

        try
        {
            var adapter = BluetoothAdapter.DefaultAdapter;
            if (adapter is null)
            {
                _pairedHint.Text = "Bluetooth unavailable on this phone.";
                return;
            }

            var devices = adapter.BondedDevices;
            if (devices is null || devices.Count == 0)
            {
                _pairedHint.Text = "No paired devices yet. Pair this phone with the PC in Android Bluetooth settings first.";
                return;
            }

            _pairedHint.Text = string.Empty;

            string? currentMac = Prefs.Get(this, "pc_mac", null);

            foreach (var device in devices.OrderBy(d => d.Name ?? string.Empty))
            {
                bool associated = !string.IsNullOrEmpty(currentMac) &&
                    string.Equals(currentMac, device.Address, StringComparison.OrdinalIgnoreCase);

                var row = new Button(this)
                {
                    Text = associated
                        ? $"{device.Name ?? "(unnamed)"} ({device.Address}) - associated"
                        : $"{device.Name ?? "(unnamed)"} ({device.Address})"
                };
                row.SetAllCaps(false);
                row.Click += (_, _) => AssociateAndRestart(device.Address);
                _pairedList.AddView(row, Pad(0, 4, 0, 0));
            }
        }
        catch
        {
            _pairedHint.Text = "Grant the Bluetooth permission to see paired devices.";
        }
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
        _macBox.Text = mac;
        RefreshPairedDevices();
        Log($"Saved {mac}. Restarting sink service...");
        Toast.MakeText(this, $"Associated with {mac}. Restarting...", ToastLength.Short)?.Show();

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
