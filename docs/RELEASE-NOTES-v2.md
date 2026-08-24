# C4P v2.0 — Cast for PC

**Turn your Windows PC into a Bluetooth speaker for your phone.**

Play your phone's music, podcasts, or videos and hear them through your PC's speakers — no cables, no apps fighting over Bluetooth menus. A tiny app on your phone controls everything with one button.

---

## What's new in v2.0

This version is a **major cleanup** that makes C4P simpler, safer, and easier to set up:

- **Way simpler setup.** No IP addresses, no pairing keys, no QR codes, no typing anything. If your phone is already paired to your PC in Bluetooth settings (like any headphones would be), you just pick it from a list. Done.
- **No firewall prompts anymore.** The old version opened a network port on your PC; this one opens **zero network ports** on both devices. Everything runs over the normal Bluetooth connection you already have.
- **More private by design.** With no Wi-Fi involvement, nothing about C4P is reachable from your network. The only devices that can connect are ones you've paired yourself.
- **Same instant Pause/Resume.** Pause sends audio back to your phone's speaker instantly and Resume flips it back to the PC — no re-connecting wait.
- **Smaller and lighter.** About 1,800 lines of code removed.

> Upgrading from v1? Just install the new APK over the old one (your settings are kept) and replace the tray app. On first open, tap **Pick your PC** once and choose your computer.

---

## How to install (plain-English guide)

### What you need
- A Windows 10/11 PC with Bluetooth
- An Android phone (Android 8.0 or newer)
- Both connected to each other in Bluetooth settings (see step 1)

### Step 1 — Pair your phone and PC (one time)

1. On your PC: **Settings → Bluetooth & devices → Add device**
2. On your phone: pull down notifications, long-press **Bluetooth**, tap your PC's name when it appears
3. Confirm the code on both screens if asked

### Step 2 — Set up the PC side

1. Download `c4p-tray-win-x64.zip` below and unzip it anywhere (Desktop is fine)
2. Double-click **`Launch C4P.bat`** — it will offer to install the free .NET runtime if your PC doesn't have it
3. You'll see a small **C4P icon** appear near the clock. That's it — it just waits for your phone now.

### Step 3 — Set up the phone side

1. Download `c4p-remote.apk` below and open it (allow "install unknown apps" if asked)
2. Open **C4P**, allow the Bluetooth + notification permissions
3. Tap **Pick your PC** and choose your PC's name from the list
4. Within seconds the notification says **Connected** — play something on your phone!

### Every day after that

Nothing to do. Open the app or swipe down: one big button pauses/resumes, and audio comes out of your PC automatically whenever you want it to.

---

## Questions people ask

- **Is this safe?** Yes. Your PC only accepts audio from phones *you* paired, and v2 opens no network doors at all.
- **Does my music go over Wi-Fi?** No. Audio travels over Bluetooth only.
- **Can I still hear notifications on my phone?** Yes — use **Pause** to instantly route sound back to the phone without disconnecting.
- **Windows shows an unsigned-app warning?** The APK is self-built and not from the Play Store, so Android may warn once. That's expected for sideloaded apps.

## Known limitations

- Volume is controlled on your phone (no speaker-volume sync)
- Uses some unofficial Android Bluetooth APIs — works today on Android 8–15, but future Android versions could change things
