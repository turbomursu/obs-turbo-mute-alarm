# OBS Turbo Mute Alarm

<p align="center"><img src="src/ObsTurboMuteAlarm/Resources/mursu-soft.png" alt="Mursu-soft" width="280"></p>

A small Windows tray app by **Mursu-soft** that warns you when an OBS audio source is muted, so
you don't stream or record half an hour of silence.

Pick any OBS audio sources (microphone, desktop audio, game audio, …). If **any** of them is
muted, a big flashing **MUTED** warning appears on the outputs you choose:

- a **popup window** on any of your monitors (one, several or all), and/or
- the DED display of a **WinWing ViperAce F-16 ICP**, showing a large flashing "MUTE" at full
  backlight brightness.

The warning disappears as soon as every watched source is unmuted.

> **100% vibe coded in Claude Opus 5.5. Use entirely at your own risk.**

## Features

- Connects to OBS's built-in WebSocket server (OBS 28 or later) and reacts to mute changes
  instantly. Reconnects automatically if OBS is restarted.
- Sources are found by their OBS UUID, so renaming a source in OBS doesn't break anything.
- Popup window:
  - always on top;
  - never takes keyboard focus and stays out of Alt+Tab;
  - clicks pass through it (optional);
  - position, size, text, opacity and flash speed are adjustable.
- The popup shows which sources are muted.
- WinWing ICP output talks to the device directly over USB HID. It works with SimAppPro running
  or closed.
- Inputs and displays are picked in one window: tick the audio inputs and click your monitors on
  a map of your real monitor layout. Other options are in Settings; there's no need to edit
  files.
- Optional "Start with Windows".

## Requirements

- Windows 10 or 11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (x64), or use a
  self-contained build (see [Building](#building))
- OBS Studio 28 or later
- Optional: WinWing ViperAce ICP (USB VID `0x4098`, PID `0xBF06`)

## Setup

1. **Enable the OBS WebSocket server.** In OBS, open **Tools → WebSocket Server Settings**, tick
   **Enable WebSocket server**, and note the port (default `4455`). If authentication is on, also
   note the password.
2. **Start `ObsTurboMuteAlarm.exe`.** On first start the **Choose inputs and displays** window
   opens and connects to OBS. Host `127.0.0.1`, port `4455` and the password from this PC's OBS
   settings (`%APPDATA%\obs-studio\plugin_config\obs-websocket\config.json`) are used
   automatically. If OBS runs on another PC or uses a different port or password, click
   **OBS connection settings…**.
3. **1. Audio inputs.** Tick the inputs to watch. The list shows microphones, desktop audio and
   application audio capture sources, plus whether each is live or muted right now. Tick
   **Show all sources with audio** to also list media, browser and game-capture sources.
4. **2. Displays.** Your monitors are drawn in the same arrangement as in Windows display
   settings. Click a monitor to select it (red = the warning pops up there); click it again to
   deselect it. **Identify monitors** shows each monitor's number on the screen itself. Tick
   **WinWing ViperAce ICP** to also flash the warning on the ICP; the window shows whether the
   ICP is connected.
5. Click **Apply & test (5 s)** to see the warning, then **Save**.

The app then lives in the system tray. Click the tray icon to open **Choose inputs and
displays**. Right-click it for Settings (OBS connection, popup look, ICP brightness, start with
Windows), the 5-second test, the log, Help, and Exit.

**Help:** right-click the tray icon → **Help…**, click **Help** in either window, or press
**F1**. The Help window has short setup and troubleshooting instructions.

## Settings

Right-click the tray icon → **Settings…**:

| Setting | Meaning |
| --- | --- |
| Host / Port / Password | OBS WebSocket connection. Leave the password empty to use the one from this PC's OBS settings. |
| Popup text | Big text in the popup (default `MUTED`). The muted source names are shown below it. |
| Position | Where on the monitor the popup appears: one of 9 positions (default top centre). |
| Size | Small, Medium or Large, relative to the monitor size. |
| Opacity % | 20–100. |
| Clicks pass through the popup | When ticked, mouse clicks go to the window underneath the popup. |
| Flash interval (ms) | Time between flashes, 100–5000 ms (default 400). |
| ICP backlight while flashing | DED backlight level 0–255 while warning (default 255 = max). |
| After the warning, set backlight to | Optionally set this DED backlight level when the warning ends. Leave unticked to keep the level the warning used. |

Settings are stored in `%APPDATA%\ObsTurboMuteAlarm\settings.json`. If you type an OBS password, it
is stored encrypted with Windows DPAPI, readable only by your Windows account. The password is
never written to the log.

The log is `%LOCALAPPDATA%\ObsTurboMuteAlarm\log.txt`.

## Notes

- **Fullscreen games.** An always-on-top window can't be drawn over a game running in
  *exclusive* fullscreen. Use *borderless windowed* (or "fullscreen windowed") mode, put the
  popup on another monitor, or use the ICP output.
- **WinWing ICP.** The app opens the ICP non-exclusively, so SimAppPro can stay running. While
  the warning shows, this app takes over the DED. When the warning ends, the DED is cleared;
  a running sim or SimAppPro redraws it on its next update. Don't run two programs that
  write to the DED at the same time.
- Only one copy of the app runs at a time.

## Command line

| Option | What it does |
| --- | --- |
| `--test` | Starts normally and shows the warning for 5 seconds on the configured outputs. |
| `--help` | Starts normally and opens the Help window. |
| `--check-obs` | Connects to OBS once, prints every source with audio (with UUID and mute state) to the console, and exits. Never touches the ICP. |
| `--preview-ded` | Prints the ICP's big "MUTE" frame as ASCII art and exits. |

## Building

Requires the .NET 8 SDK.

```powershell
dotnet build ObsTurboMuteAlarm.sln
dotnet test ObsTurboMuteAlarm.sln

# Single-file build that needs the .NET 8 Desktop Runtime:
dotnet publish src\ObsTurboMuteAlarm -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish

# Self-contained build (no runtime install needed, larger file):
dotnet publish src\ObsTurboMuteAlarm -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

## How it works

- **OBS:** obs-websocket v5 protocol. The app authenticates, subscribes to `InputMuteStateChanged`
  events, and reads the starting state with `GetInputMute`.
- **ICP:** sends 64-byte HID reports that write a 200×65 pixel 1-bit frame to the DED, plus a
  backlight brightness report. The big "MUTE" glyphs span the whole display and alternate
  between normal and inverse video.

## Credits

- WinWing ICP / DED HID protocol code adapted from
  [BmsLightBridge](https://github.com/RedMeKool/BmsLightBridge) by RedMeKool (MIT License). See
  [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
- [HidSharp](https://www.zer7.com/software/hidsharp) for USB HID access.

Not affiliated with OBS Project or WinWing.

## License

Public domain: [The Unlicense](LICENSE). Use, copy, modify, sell or redistribute it for any
purpose, with no conditions and no attribution needed.

The ICP code adapted from BmsLightBridge (`src/ObsTurboMuteAlarm/Icp/IcpHidDevice.cs`,
`DedCommand.cs`, `IcpDisplay.cs`) stays under its original MIT License; keep that notice if you
redistribute those files. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
