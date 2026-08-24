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

    private const string A2dpSinkUuid = "0000110b-0000-1000-8000-00805f9b34fb";

    private TextView _statusView = null!;
    private Button _pauseButton = null!;
    private Button _setupToggle = null!;
    private LinearLayout _setupSection = null!;
    private Button _logToggle = null!;
    private LinearLayout _logSection = null!;
    private TextView _logView = null!;

    private readonly Handler _uiHandler = new(Looper.MainLooper!);
    private Java.Lang.Runnable? _tick;
    private bool _active;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(BuildLayout());
        Prefs.Remove(this, "pc_ip", "pc_key");
        EnsurePermissionsAndMaybeStartService();

        if (string.IsNullOrEmpty(Prefs.Get(this, "pc_mac", null)) && HasBluetoothPermission())
            ShowPcPicker();
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

        var hint = new TextView(this)
        {
            Text = "Pair your PC in Android Bluetooth settings first,\nthen tap below and choose it.",
            TextSize = 13f
        };
        _setupSection.AddView(hint);

        var pickButton = new Button(this) { Text = "Pick your PC" };
        pickButton.Click += (_, _) => ShowPcPicker();
        _setupSection.AddView(pickButton, Pad(0, 8, 0, 0));

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

    private bool HasBluetoothPermission()
    {
        return (int)Build.VERSION.SdkInt < 31 ||
               CheckSelfPermission(Manifest.Permission.BluetoothConnect) == Permission.Granted;
    }

    private void ShowPcPicker()
    {
        if (!HasBluetoothPermission())
        {
            Toast.MakeText(this, "Grant the Bluetooth permission first.", ToastLength.Long)?.Show();
            RequestPermissions(new[] { Manifest.Permission.BluetoothConnect }, PermissionRequestCode);
            return;
        }

        ICollection<BluetoothDevice> bonded;
        try
        {
            bonded = BluetoothAdapter.DefaultAdapter!.BondedDevices!;
        }
        catch (Exception ex)
        {
            Log($"Picker failed: {ex.Message}");
            Toast.MakeText(this, "Could not list paired devices.", ToastLength.Long)?.Show();
            return;
        }

        if (bonded is null || bonded.Count == 0)
        {
            Toast.MakeText(this,
                "No paired devices. Pair your PC in Android Bluetooth settings first.",
                ToastLength.Long)?.Show();
            return;
        }

        List<BluetoothDevice> candidates = new();

        foreach (BluetoothDevice device in bonded)
        {
            bool isA2dpSink = false;

            try
            {
                ParcelUuid[]? uuids = device.GetUuids();

                if (uuids != null)
                {
                    foreach (ParcelUuid uuid in uuids)
                    {
                        if (uuid?.Uuid?.ToString()?.Equals(A2dpSinkUuid, StringComparison.OrdinalIgnoreCase) == true)
                        {
                            isA2dpSink = true;
                            break;
                        }
                    }
                }
            }
            catch
            {
            }

            if (isA2dpSink)
                candidates.Add(device);
        }

        if (candidates.Count == 0)
            candidates.AddRange(bonded);

        var labels = new string[candidates.Count];

        for (int i = 0; i < candidates.Count; i++)
        {
            BluetoothDevice device = candidates[i];
            string name;

            try
            {
                name = device.Name ?? device.Address;
            }
            catch
            {
                name = device.Address;
            }

            labels[i] = $"{name}  ({device.Address})";
        }

        new AlertDialog.Builder(this)
            .SetTitle("Paired devices")
            .SetItems(labels, (_, e) =>
            {
                BluetoothDevice chosen = candidates[e.Which];
                Log($"Associated with {labels[e.Which]}.");
                AssociateAndRestart(chosen.Address);
            })
            .SetNegativeButton("Cancel", (_, _) => { })
            .Show();
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
        bool bluetoothReady = HasBluetoothPermission();

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

        if (!bluetoothGranted)
            return;

        StartForegroundService(new Intent(this, typeof(SinkWatchService)));

        if (string.IsNullOrEmpty(Prefs.Get(this, "pc_mac", null)))
            ShowPcPicker();
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
