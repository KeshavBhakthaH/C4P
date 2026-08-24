# C4P Simplification Brief — LAN Layer Removed

Date: 2026-08-25
Supersedes: [HARDENING-BRIEF.md](HARDENING-BRIEF.md)

## Why

Code review showed the audio path never needed the network: audio flows over OS-level
Bluetooth A2DP, the phone drives connect/pause/resume via its own BT profile proxy
(`setActiveDevice` flips), and the PC's sink self-hosts with an auto-heal loop. The LAN
channel (TCP control port, UDP discovery, PSK + QR pairing) only served setup convenience,
a status/test display, and vestigial remote commands that nothing ever sent.

## What was removed

- **PC (`pc-tray/`)**: `CommandServer.cs`, `DiscoveryResponder.cs`, `PairingKey.cs`,
  `PairingQr.cs`, `QrForm.cs`, `NetGuard.cs`; "Show pairing QR..."/"Copy pairing key" menu
  items; QRCoder + ProtectedData packages. Tray now deletes the legacy
  `%APPDATA%\C4P\pairing-key.txt` on launch.
- **Phone (`android-remote/`)**: `PcClient.cs`, `PcDiscovery.cs`, `PcPairing.cs`,
  `ScanPairingActivity.cs`; scan/IP/key/find-PC/test-link UI. Manifest drops `INTERNET`,
  `ACCESS_NETWORK_STATE`, `ACCESS_WIFI_STATE`, `CHANGE_WIFI_MULTICAST_STATE`, and `CAMERA`
  permissions.
- Stale prefs (`pc_ip`, `pc_key`) are wiped on app start.

## What replaced it

Setup is now: pair phone ↔ PC in normal Android Bluetooth settings, then in the app tap
**Pick your PC** and choose it from the bonded-devices dialog (A2DP-sink-role devices listed
first). The existing association/watchdog/pause-resume machinery is untouched.

## Security posture after removal

Zero open network sockets on both ends. No key to leak, no protocol to harden, no firewall
rules. Remaining surface = stock Bluetooth (OS-managed pairing, link keys), identical to any
consumer BT speaker. The entire August 2024 hardening scope (session MACs, IP gating, rate
limits, DPAPI storage) became moot by deletion.

## Unchanged behavior (verified)

Audio routing, watchdog auto-reconnect with backoff, instant pause/resume via
`setActiveDevice`, tray menu Connect/Disconnect/Pause, live status line (local BT state),
notification controls.

## Net diff

~1,600 lines removed, ~130 added across both apps.
