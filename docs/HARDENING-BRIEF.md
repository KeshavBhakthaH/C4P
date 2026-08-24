# C4P Security Hardening Brief

Date: 2026-08-24
Scope: `pc-tray/` + `android-remote/` — fixes for the issues found in the August 2026 security review, with minimal changes to app logic.

## What changed

### Wire protocol (both apps must be updated together)

1. **Session MAC — every command and response is now authenticated** (`CommandServer.cs`, `PcClient.cs`)
   - Handshake is unchanged: `CHALLENGE <nonce>` → `AUTH hmac(key, nonce)` → `OK READY`.
   - Both sides then derive `sessionKey = HMAC-SHA256(key, nonce ‖ 0x01)`.
   - Phone sends `COMMAND|<hmac(sessionKey, command)>`; PC replies `REPLY|<hmac(sessionKey, reply)>`. Tags verified with constant-time compare.
   - Effect: an on-path attacker can no longer relay the handshake and then tamper with or inject commands/responses. Tampering → `ERR TAMPERED`; unsigned legacy lines → `ERR BAD_REQUEST`.

2. **Authenticated discovery** (`DiscoveryResponder.cs`, `PcDiscovery.cs`, `MainActivity.cs`)
   - Query is now `C4P_DISCOVER <random-nonce>`; the PC replies `C4P_ANNOUNCE <name>|<hmac(key, "C4P_ANNOUNCE"‖nonce)>`.
   - The phone verifies announces once a pairing key is stored; unverified announcers are dropped, so a rogue host can no longer poison the saved PC IP or spoof "Found PC". Pre-pairing (no key yet), discovery still works unverified.
   - Note: if no key is stored the announce tag is ignored for naming but the IP is only trusted after pairing anyway.

3. **LAN-only listener gate** (`NetGuard.cs` new, `CommandServer.cs`)
   - TCP connections from non-private/non-loopback source addresses are rejected before the handshake (same rule set the UDP responder already used). The old `SO_REUSEADDR` socket option was removed to prevent local port hijacking.

### Resource protection (`CommandServer.cs`)

4. **Concurrency cap**: max 8 concurrent client sessions; excess connections dropped immediately.
5. **Failed-handshake lockout**: 5 failed AUTHs from one IP → that IP is ignored for 60 seconds (lazy-pruned table).

### Secret handling

6. **PC key file encrypted** (`PairingKey.cs`, + NuGet `System.Security.Cryptography.ProtectedData`)
   - `%APPDATA%\C4P\pairing-key.txt` now holds a DPAPI-protected blob (current user). An existing plaintext file migrates automatically on first run — **no re-pairing needed**.
7. **Phone hardening** (`AndroidManifest.xml`, `MainActivity.cs`)
   - `android:allowBackup="false"` — the key/prefs never leave the device via cloud or device-to-device transfer.
   - Pairing-key field masked; "Show key" toggle added.
8. **Clipboard auto-clear** (`TrayContext.cs`) — "Copy pairing key" clears the Windows clipboard after 30 s if the key is still there.

## Compatibility

- New APK ↔ new tray: fully compatible. QR payload format unchanged.
- Old APK ↔ new tray (or vice versa): commands fail closed (`AUTH_FAILED` / `BAD_REQUEST` / tamper errors). Update both together; re-scanning the QR is not required.

## Verification

- `dotnet build` clean for both projects (0 errors; pre-existing Xamarin package warnings unchanged).
- Protocol harness (`%TEMP%\opencode\c4p-proto-test`) mirroring both sides' exact crypto logic — 8/8 passed:
  happy-path STATUS, command-tamper rejection, wrong-key rejection, unsigned-command rejection, signed unknown-command handling, discovery HMAC verify + forged-announce rejection, IP lockout after repeated failures.

## Known residuals (accepted)

- Traffic content after auth remains non-confidential by design (commands/status are not secret); integrity is now guaranteed. Full TLS was deliberately skipped as overkill for LAN audio control.
- Discovery machine-name is still sent in clear to LAN peers (authenticated reply).
- APK remains debug-signed (sideload distribution model); release signing deferred pending keystore decision.
- Phone prefs are sandboxed plaintext (not EncryptedSharedPreferences); acceptable given private-mode storage plus backup exclusion.

## Smoke test checklist for next phone↔PC session

1. Reinstall both: publish tray (`dotnet publish pc-tray/A2dpSink.csproj -c Release`) and APK (`dotnet publish android-remote/A2dpRemote.csproj -c Release -r android-arm64`).
2. Launch tray — confirm existing key keeps working (no re-pair prompt), `pairing-key.txt` now DPAPI blob.
3. App: Scan pairing QR → should pair and STATUS test pass; Pause/Resume from notification.
4. "Find PC automatically" → finds and saves the PC (announce verified).
5. Optional: kill Wi-Fi firewall rules off-LAN → connection refused/gated.
