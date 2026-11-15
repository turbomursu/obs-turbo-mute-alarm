# OBS Turbo Mute Alarm – Instructions

OBS Turbo Mute Alarm shows a big flashing warning when one of your OBS audio sources is muted.
The warning can appear on any of your monitors, and on a WinWing ViperAce F-16 ICP display.

100% vibe coded in Claude Opus 5.5. Use entirely at your own risk.

## Install

1. Download `ObsTurboMuteAlarm-Setup-<version>.exe` from
   https://github.com/turbomursu/obs-turbo-mute-alarm/releases/latest
2. Run it. The installer isn't code-signed, so Windows may say "Windows protected your PC".
   Click **More info**, then **Run anyway**.
3. Choose where to install. Everything goes into that one folder. You don't need admin rights
   unless you choose a folder like Program Files.
4. Choose what to install:
   - **Monitor popups only** (default): the warning appears on your monitors.
   - **Monitor popups and WinWing ViperAce ICP display**: also adds ICP support. Only choose
     this if you have the ICP.
5. Optionally tick **desktop shortcut** and **Start automatically when Windows starts**.
6. Click **Install**, then **Finish**. The app starts.

Nothing else is needed: the .NET runtime is built in.

## First start

1. In OBS, open **Tools → WebSocket Server Settings** and tick **Enable WebSocket server**.
   Click **OK**.
2. The **Choose inputs and displays** window opens. It connects to OBS on this PC
   automatically, including the password.
3. Under **1. Audio inputs**, tick the sources to watch, such as your microphone.
4. Under **2. Displays**, click the monitors where the warning should appear. Red means
   selected. If you installed ICP support, you can also tick **WinWing ViperAce ICP**.
5. Click **Apply & test (5 s)** to see the warning. Then click **Save**.

## Daily use

The app runs in the system tray (near the clock). Start OBS as usual. If any watched source is
muted, the warning flashes until you unmute it.

Tray icon colours:

- **Green**: connected to OBS, nothing muted.
- **Red**: a watched source is muted.
- **Grey**: not connected to OBS. The app keeps retrying.

Click the tray icon to change inputs and displays. Right-click it for **Settings** (popup size,
position, text, flash speed, OBS connection, start with Windows), **Test**, **Help** and **Exit**.

Tip: an always-on-top popup can't appear over a game in *exclusive* fullscreen mode. Use
borderless/windowed fullscreen, or put the warning on another monitor or the ICP.

## Add or remove ICP support later

Run the installer again, choose the same folder, and pick the option you want. Your settings
are kept.

## Uninstall

Open Windows **Settings → Apps → Installed apps**, find **OBS Turbo Mute Alarm**, and click
**Uninstall**. The uninstaller asks whether to also delete your settings and log.

## Files

- Program: the folder you chose when installing.
- Settings: `%APPDATA%\ObsTurboMuteAlarm\settings.json`
- Log: `%LOCALAPPDATA%\ObsTurboMuteAlarm\log.txt`

More information: https://github.com/turbomursu/obs-turbo-mute-alarm
