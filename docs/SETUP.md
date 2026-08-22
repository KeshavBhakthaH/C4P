# C4P Setup Guide

## 1. Pair first, launch second

Pair the phone and PC through the normal OS Bluetooth settings **before** starting anything C4P-related.

> Pairing while the sink app is running can leave a half-open bridge that poisons the phone's A2DP profile cache until you toggle phone Bluetooth or forget/re-pair. This is the single most common failure mode - avoid it.

## 2. PC tray app

- One-time prerequisite: install the [.NET 8 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) if you don't already have it (check with `dotnet --list-runtimes` — look for `Microsoft.WindowsDesktop.App 8.x`).
- Download `c4p-tray-win-x64.zip` (~6 MB) from [Releases](../../releases), unzip anywhere, run `C4P.exe`.
- First launch: Windows may prompt to allow inbound TCP 8080 for LAN status queries. Allow it (private networks). To add manually, run as admin:

```powershell
netsh advfirewall firewall add rule name="C4P" dir=in action=allow protocol=TCP localport=8080
```

The tray icon appears with menu items: Connect / Disconnect / Pause forwarding / Resume forwarding / Exit. Single instance enforced.

## 3. Phone app

- Install `c4p-remote.apk` from [Releases](../../releases) ("install unknown apps" permission required; Play Protect may show an unsigned-publisher warning because it is debug-signed).
- Open the app, grant `BLUETOOTH_CONNECT` (+ notifications on Android 13+).

### Find your PC's Bluetooth MAC

On the phone: **Bluetooth settings > gear icon next to the PC > MAC address**.
(Format like `1A:2B:3C:4D:5E:6F`.)

### Find your PC's LAN IP

On the PC run `ipconfig` and use the IPv4 of your active adapter (Wi-Fi/Ethernet), e.g. `192.168.1.50`.

### Enter both in the app

Open the app > **Show setup** > fill IP + MAC > **Save MAC + restart sink service**.

Within seconds the notification should flip from *Connecting...* to **Connected - \<PC name\>**. Play something on the phone; audio should come out of the PC.

## 4. Everyday usage

- The app screen shows live status and one Pause/Resume toggle. Everything also works from the persistent notification without opening the app.
- **Pause** sends audio back to the phone's speaker instantly (the BT link stays up). **Resume** flips it back to the PC. Neither tears down the profile.
- If you pause from the PC tray instead ("Pause forwarding"), the sink bridge closes and audio returns to the phone; resume reopens it.

## 5. Troubleshooting

Order matters - try these in sequence:

1. **Phone Bluetooth off/on.** Fixes most zombie A2DP states.
2. **Reopen the phone app** (it nudges the watchdog into reconnecting immediately).
3. **Forget/re-pair** on both ends. Last resort before services.
4. Restart Windows Bluetooth services (admin PowerShell):

```powershell
Restart-Service bthserv, BthAvctpSvc, BTAGService
```

5. Status stuck "Connecting" for minutes? Check the phone app's **Log** section - every attempt is timestamped there.

## 6. Dev-only preference injection

For development you can push prefs straight onto a debug build instead of typing them:

```bash
adb push inject-prefs.example.xml /data/local/tmp/prefs.xml
adb shell "run-as dev.a2dpremote.remote sh -c 'cp /data/local/tmp/prefs.xml shared_prefs/a2dp_remote_prefs.xml'"
```

Edit `inject-prefs.example.xml` first and put in your real IP/MAC. Only works on debug builds.

## 7. Known limitations

- TCP status protocol is unauthenticated - home networks only.
- No AVRCP metadata/volume sync; volume is controlled at the source device.
- Hidden Android APIs (`setActiveDevice`, reflective `connect`) are used deliberately; they work today but are formally unsupported by Google and could change in future Android versions. The code falls back to profile disconnect when they are unavailable.
