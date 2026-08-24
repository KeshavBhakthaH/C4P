# C4P - Cast for PC

Turn a Windows PC into a Bluetooth speaker for your phone, with a tiny Android remote that owns the connection.

C4P routes your phone's audio (music, podcasts) through your PC's output. Built for people who work on a PC all day and want phone audio coming out of the same speakers - without fighting Windows Bluetooth menus every time.

```
Phone ──(A2DP over Bluetooth)──> PC (acts as an A2DP sink / "BT speaker")
  │                                  │
  └─ Android remote app              └─ Tray app hosts the sink
     drives connect/pause/resume
```

## Components

| Folder | What it is | Tech |
|---|---|---|
| `pc-tray/` | Windows tray app that hosts the A2DP sink (`AudioPlaybackConnection`) and auto-connects | C# / .NET 8 + WinForms NotifyIcon |
| `android-remote/` | Android foreground service that acquires the A2DP profile proxy and drives `connect()`/`disconnect()`/`setActiveDevice()` | C# / net8.0-android |

**Design inversion:** the *phone* owns the Bluetooth link; the PC stays a passive host. This avoids the classic "PC connects to phone" race conditions where the sink bridge opens after the phone has already attached its stream.

Key behaviors:
- **Pause/Resume = routing flip, not teardown.** Pause calls `setActiveDevice(null)` so audio returns to the phone speaker while the BT link stays alive. Resume flips it back. No profile churn, no re-pairing.
- **Self-healing watchdog** on the phone with exponential backoff (10s → 60s cap).
- **Tiny footprint:** tray app ~11 MB private working set (self-contained publish).

## Requirements

**PC side**
- Windows 10 2004+ or Windows 11
- A Bluetooth adapter
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) (x64) — one-time install

**Phone side**
- Android 8.0+ (API 26); tested on API 34
- Runtime permission: `BLUETOOTH_CONNECT` (Android 12+) and notifications

Both devices must be **paired beforehand** in normal OS settings. Never pair while the tray app is running - pair first, launch second.

## Setup

See [docs/SETUP.md](docs/SETUP.md) for the full walkthrough, or [docs/INSTALL-GUIDE.md](docs/INSTALL-GUIDE.md) for a beginner-friendly step-by-step guide with screenshots-level detail. Short version:

1. Pair phone ↔ PC in Windows/Android Bluetooth settings.
2. Run `pc-tray`'s `Launch C4P.bat` — it starts `C4P.exe` directly if the .NET 8 Desktop Runtime is present, and otherwise offers to download and install it automatically (plain `C4P.exe` also works once the runtime is installed).
3. Install `android-remote` APK.
4. In the app: open **Setup**, tap **Pick your PC**, and choose your PC from your existing paired devices.
5. Audio should route to the PC within seconds. Control via the notification or the single Pause/Resume button.

## Security note

The app opens **zero network ports** on both ends. All control traffic (connect, pause, resume, status) rides on the OS-level Bluetooth link between two devices that are paired in normal system settings - there is no LAN listener, no pairing key, and no discovery protocol to attack. Trust is exactly the trust you already granted when you paired the phone and PC in Bluetooth settings.

## Building from source

Prereqs: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0). The Android project also needs the Android SDK (auto-detected via `ANDROID_HOME` or default install path).

```bash
# PC tray (framework-dependent build for dev)
dotnet build pc-tray/A2dpSink.csproj

# PC tray (framework-dependent, like the release artifact; needs .NET 8 Desktop Runtime on target)
dotnet publish pc-tray/A2dpSink.csproj -c Release

# Self-contained variant (~170 MB, zero prerequisites)
dotnet publish pc-tray/A2dpSink.csproj -c Release -r win-x64 --self-contained true

# Android APK (arm64-only Release, debug-signed, sideloadable)
dotnet publish android-remote/A2dpRemote.csproj -c Release -r android-arm64
# -> android-remote/bin/Release/net8.0-android/android-arm64/publish/dev.a2dpremote.remote-Signed.apk
```

If your Android SDK lives somewhere unusual, pass it explicitly:
`-p:AndroidSdkDirectory=C:\path\to\android-sdk`

## License

[MIT](LICENSE)
