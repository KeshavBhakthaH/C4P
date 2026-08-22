using Android.Bluetooth;
using Android.Content;
using Android.Runtime;
using Java.Lang;

namespace A2dpRemote;

public class A2dpConnector : Java.Lang.Object, IBluetoothProfileServiceListener
{
    private readonly Context _context;
    private BluetoothA2dp? _proxy;
    private BroadcastReceiver? _stateReceiver;
    private bool _proxyPending;
    private Java.Lang.Reflect.Method? _setActiveMethod;
    private Java.Lang.Reflect.Method? _getActiveMethod;

        public event Action<BluetoothDevice, int>? ConnectionStateChanged;
        public event Action<string>? LogMessage;

        public bool HasProxy => _proxy is not null;

        public A2dpConnector(Context context)
    {
        _context = context;
    }

    public void AcquireProxy()
    {
        if (_proxy is not null || _proxyPending)
            return;

        var adapter = BluetoothAdapter.DefaultAdapter;
        if (adapter is null)
        {
            LogMessage?.Invoke("Bluetooth adapter unavailable.");
            return;
        }

        _proxyPending = true;
        try
        {
            adapter.GetProfileProxy(_context, this, ProfileType.A2dp);
        }
        catch (System.Exception ex)
        {
            _proxyPending = false;
            LogMessage?.Invoke($"AcquireProxy failed: {ex.Message}");
        }
    }

    public void Release()
    {
        UnregisterStateReceiver();

        if (_proxy is not null)
        {
            try { BluetoothAdapter.DefaultAdapter?.CloseProfileProxy(ProfileType.A2dp, _proxy); } catch { }
            _proxy = null;
        }

        _proxyPending = false;
    }

    public void OnServiceConnected([GeneratedEnum] ProfileType profile, IBluetoothProfile? proxy)
    {
        if (profile != ProfileType.A2dp || proxy is not BluetoothA2dp a2dp)
            return;

        _proxy = a2dp;
        _proxyPending = false;
        LogMessage?.Invoke("A2DP proxy acquired.");
        RegisterStateReceiver();
    }

    public void OnServiceDisconnected([GeneratedEnum] ProfileType profile)
    {
        _proxy = null;
        LogMessage?.Invoke("A2DP proxy lost.");
    }

    public bool IsConnected(BluetoothDevice device)
    {
        if (_proxy is null)
            return false;

        try
        {
            var connected = _proxy.GetDevicesMatchingConnectionStates(new[]
            {
                ProfileState.Connected,
                ProfileState.Connecting
            });

            foreach (var candidate in connected)
            {
                if (candidate.Address == device.Address)
                    return true;
            }
        }
        catch (System.Exception ex)
        {
            LogMessage?.Invoke($"IsConnected failed: {ex.Message}");
        }

        return false;
    }

    public bool InvokeHidden(string method, BluetoothDevice device)
    {
        if (_proxy is null)
        {
            LogMessage?.Invoke("No A2DP proxy yet.");
            return false;
        }

        try
        {
            var declared = _proxy.Class.GetDeclaredMethod(method, Class.FromType(typeof(BluetoothDevice)));
            Java.Lang.Reflect.AccessibleObject.SetAccessible(new[] { declared }, true);
            declared.Invoke(_proxy, device);
            return true;
        }
        catch (System.Exception ex)
        {
            LogMessage?.Invoke($"{method}() failed: {ex.Message}");
            return false;
        }
    }

    public bool TrySetActiveDevice(BluetoothDevice? device)
    {
        if (_proxy is null)
        {
            LogMessage?.Invoke("No A2DP proxy yet.");
            return false;
        }

        try
        {
            _setActiveMethod ??= FindHiddenMethod("setActiveDevice", Class.FromType(typeof(BluetoothDevice)));
            if (_setActiveMethod is null)
                return false;

            _setActiveMethod.Invoke(_proxy, new Java.Lang.Object?[] { device });
            return true;
        }
        catch (System.Exception ex)
        {
            LogMessage?.Invoke($"setActiveDevice failed: {ex.Message}");
            return false;
        }
    }

    public BluetoothDevice? GetActiveDevice()
    {
        if (_proxy is null)
            return null;

        try
        {
            _getActiveMethod ??= FindHiddenMethod("getActiveDevice");
            if (_getActiveMethod is null)
                return null;

            return _getActiveMethod.Invoke(_proxy)?.JavaCast<BluetoothDevice>();
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    private Java.Lang.Reflect.Method? FindHiddenMethod(string name, params Class[] paramTypes)
    {
        try
        {
            var method = _proxy!.Class.GetDeclaredMethod(name, paramTypes);
            Java.Lang.Reflect.AccessibleObject.SetAccessible(new[] { method }, true);
            LogMessage?.Invoke($"Resolved hidden method: {name}.");
            return method;
        }
        catch (System.Exception ex)
        {
            LogMessage?.Invoke($"Hidden method {name} unavailable: {ex.Message}");
            return null;
        }
    }

    private void RegisterStateReceiver()
    {
        if (_stateReceiver is not null)
            return;

        _stateReceiver = new StateReceiver(OnStateBroadcast);

        var filter = new IntentFilter(BluetoothA2dp.ActionConnectionStateChanged);
        _context.RegisterReceiver(_stateReceiver, filter);
    }

    private void UnregisterStateReceiver()
    {
        if (_stateReceiver is null)
            return;

        try { _context.UnregisterReceiver(_stateReceiver); } catch { }
        _stateReceiver = null;
    }

    private void OnStateBroadcast(Intent intent)
    {
        var device = (BluetoothDevice?)intent.GetParcelableExtra(BluetoothDevice.ExtraDevice);
        int state = intent.GetIntExtra(BluetoothProfile.ExtraState, -1);

        if (device is not null && state != -1)
            ConnectionStateChanged?.Invoke(device, state);
    }

    private sealed class StateReceiver(Action<Intent> handler) : BroadcastReceiver
    {
        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent is not null)
                handler(intent);
        }
    }
}
