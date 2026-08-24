# How to Install C4P (Step-by-Step Guide)

New to this? No problem. This guide assumes you've never done anything like this before.
Follow it top to bottom — total time is about 10 minutes.

---

## What this app does

C4P lets your **phone** use your **PC's speakers** as if they were a Bluetooth speaker.
Music on your phone comes out of your computer. One button sends the sound back to the
phone when you want it.

---

## Before you start — checklist

| You need | Why |
|---|---|
| A Windows 10 or 11 PC | Runs the "speaker" part |
| Bluetooth on that PC | Almost all laptops have it; desktop PCs may need a cheap USB Bluetooth dongle |
| An Android phone (8.0 or newer) | Runs the remote control |
| Both devices charged & near each other | Bluetooth range |

---

## Part 1 — Introduce your phone and PC (Bluetooth pairing)

This is the same thing you'd do with wireless headphones.

1. On your **PC**: click **Start → Settings → Bluetooth & devices → Add device**
2. On your **phone**: swipe down from the top of the screen, then **press and hold** the Bluetooth icon until Bluetooth settings open. Make sure Bluetooth is ON.
3. Wait for your PC's name to appear on the phone under "Available devices", then tap it.
4. A number code may appear on both screens. If it matches, tap **Pair** / click **Yes** on both.
5. You'll see your PC listed under "Paired devices" on the phone. ✅

> ⚠️ **Do this BEFORE installing anything below.** If things go wrong later, 9 times out of 10 it's because this step was done out of order.

---

## Part 2 — Set up the PC (5 minutes)

### Download

1. Go to the [Releases page](../../releases) of this project.
2. Under the newest version, download **`c4p-tray-win-x64.zip`**.

### Unzip

3. Find the downloaded file (usually in your **Downloads** folder).
4. Right-click it → **Extract All...** → **Extract**. A folder opens.

### Run

5. In that folder, double-click **`Launch C4P.bat`** (it has a gear icon).
   - If Windows asks "Do you want to allow this app...", click **More info → Run anyway** (the app isn't store-signed because you build/download it directly).
6. First launch only: a black window may offer to **install the .NET Desktop Runtime** (a free, safe Microsoft component C4P needs). Click yes and let it finish, then run `Launch C4P.bat` again.

### Recognize it worked

7. Look at the bottom-right corner of your screen near the clock. You may need to click the **^ arrow** to see hidden icons.
8. You should see a small **C4P icon**. Right-click it and you'll see: *Connect / Disconnect / Pause forwarding / Exit*.

That's the PC done. It now just waits for your phone — you never need to touch it again.

---

## Part 3 — Set up the phone (3 minutes)

### Allow app installs

Android calls apps outside the Play Store "unknown apps". You'll allow them just once:

1. On your phone, go to the [Releases page](../../releases) and download **`c4p-remote.apk`**
   *(or download on the PC and copy it to the phone with a USB cable)*
2. Tap the downloaded file. Android will say something like *"For your security, your phone isn't allowed to install unknown apps from this source."*
3. Tap **Settings** on that message → turn ON **"Allow from this source"** → go back → tap **Install**.

*(You may also see a Play Protect warning about an unsigned app — tap **Install anyway**. This happens because the app is self-published rather than on the Play Store.)*

### First run

4. Open **C4P** from your app list.
5. When asked, **Allow** the Bluetooth permission and the notification permission.
6. The Setup screen opens automatically. Tap **Pick your PC**, then tap your PC's name in the list.
7. Watch the notification at the top of your phone — within seconds it should change to **Connected - \<your PC name\>**. 🎉

### Test it

8. Play any song or video **on your phone**.
9. Sound should come out of your **PC's speakers**. Done!

---

## Using it every day

- **Sound goes to the PC automatically** whenever both are on and connected.
- Open C4P (or its notification) and press the big button:
  - **Pause forwarding** = sound instantly returns to your phone's speaker
  - **Resume forwarding** = sound goes back to the PC
- To stop completely: pause, or right-click the tray icon on the PC → **Exit**.

---

## Something not working?

Try these in order — each one fixes most problems:

1. **No sound?** Turn your phone's Bluetooth off and back on. This clears 90% of issues.
2. **Still nothing?** Close the C4P app on the phone and reopen it.
3. **Notification stuck on "Connecting..."?** Make sure the PC side is running (C4P icon near the clock) and your PC's Bluetooth is on.
4. **Last resort:** "Forget"/unpair the phone and PC in Bluetooth settings on BOTH sides, restart both, then redo Part 1 and tap **Pick your PC** again.
5. Check the **Log** section inside the C4P app — every attempt is written there with a time stamp, which makes it easy to see where it stops.

---

## How to uninstall

- **Phone:** long-press the C4P icon → Uninstall.
- **PC:** right-click the tray icon → Exit, then delete the unzipped folder. Nothing else was installed on your PC (unless you accepted the .NET runtime, which other apps can also use).

---

*Still stuck? Open an [issue](../../issues) and paste what the Log section says.*
