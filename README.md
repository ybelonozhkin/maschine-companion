# Maschine Mikro MK3 Companion

This small Windows tray app gives your Mikro a standby light and a launch button.

The <img src="assets/maschine-button.svg" alt="Maschine" width="20" height="20"> button lights up in standby. Hold it to launch Maschine, and hold it again to close it.

Pair it with my [Maschine Mikro Browser](https://github.com/ybelonozhkin/maschine-mikro-browser) mod to browse sounds, manage effects and answer the save prompt from the controller too.

## Features

- Detects an already connected Mikro MK3 controller and USB reconnection.
- Shows a dim Maschine button while waiting to launch.
- Launches a configurable executable after a 1.2-second hold (configurable).
- Brings the newly launched Maschine main window to the foreground.
- Requests normal exit after a 2-second hold. The configured application handles unsaved changes through its own dialog.
- Prevents repeated actions during one hold and duplicate companion instances.
- Provides tray status, local configuration, and timestamped logs.

See [CHANGELOG.md](CHANGELOG.md) for changes.

## Requirements

- Windows 11 x64 with .NET Framework 4.x available.
- Maschine Mikro MK3 with the official Native Instruments Windows driver.
- Maschine installed locally, or another executable configured for launch.
- A writable folder for the utility, configuration, and logs.

## Build from source

Clone or download this repository into a writable folder.

Use **Windows PowerShell 5.1** on x64 Windows with the .NET Framework compiler and desktop assemblies installed. No .NET SDK or NuGet restore is required. The build currently targets Windows .NET Framework 4.x, not modern .NET.

Exit the companion from its tray menu before rebuilding, then open PowerShell in the project folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Companion.ps1
```

The script creates `companion.ini` from `companion.example.ini` if it is missing, compiles and runs the logic tests, then builds the GUI executable:

- `bin\MaschineCompanion.exe` — tray application.
- `bin\CompanionTests.exe` — hardware-independent tests.

## Run

1. Edit `companion.ini` if Maschine is installed outside the default location.
2. Run `Start-Companion.cmd`. The companion starts silently and stays in the system tray; it does not show a startup notification or open a persistent window. Expand hidden tray icons if necessary.
3. Connect the controller. With Maschine closed, the Maschine button lights dimly.
4. Hold the button for 1.2 seconds to launch. Release it before another action.
5. With the configured application running, hold for 2 seconds to request exit. If one instance is open in the background, the companion brings it forward first. Answer any save prompt in the application.

Tray menu: **Pause for 1 hour**, **Resume**, **Show status**, **Reload config**, **Open log folder**, **Start with Windows**, and **Exit**. A second launch reuses the existing instance by exiting immediately. Administrator access is not required. Windows startup can be enabled or disabled with **Start with Windows**; it is off unless explicitly enabled. Notifications are reserved for launch and configuration reload errors; an unrecoverable startup error opens an error message. **Show status** opens a window only when selected.

## Configuration

The utility reads `companion.ini` beside the launcher at startup. Use **Reload config** after editing. An invalid reload keeps the previous settings. Reload cancels an in-progress button hold; release and press again.

```ini
Executable=C:\Program Files\Native Instruments\Maschine 3\Maschine 3.exe
Arguments=
HoldMilliseconds=1200
StandbyLed=true
StandbyBrightness=Dim
EnableClose=true
CloseHoldMilliseconds=2000
PauseWhenDawRunning=true
DawProcesses=Ableton Live*;FL;FL64;FL (scaled);FL64 (scaled);FL (compatible memory);reaper;Cubase*;Nuendo*;Studio One;Bitwig Studio;ProTools;Cakewalk;Sonar
```

| Setting | Meaning |
| --- | --- |
| `Executable` | Absolute path to an existing `.exe`. Environment variables are expanded. |
| `Arguments` | Optional arguments passed directly to the executable. |
| `HoldMilliseconds` | Launch hold duration, from 500 to 10000 ms. |
| `StandbyLed` | Enable or disable standby LED writes. |
| `StandbyBrightness` | `Dim` (default), `Normal`, or `Bright`; values are case-insensitive. |
| `EnableClose` | Enable normal close requests for the application in `Executable`. |
| `CloseHoldMilliseconds` | Exit hold duration, from 1000 to 10000 ms. |
| `PauseWhenDawRunning` | `true` (default) pauses actions and hands off standby lighting when a listed DAW runs; `false` ignores the DAW list. |
| `DawProcesses` | DAW process names separated by `;`. A trailing `*` matches a prefix. Defaults cover the DAWs listed below. Empty disables this guard. |

Launch checks process names for the configured executable and Maschine 2/3. An existing process blocks launch even if it has no visible window. A 15-second cooldown follows each launch attempt. Configure a direct application executable rather than a launcher that hands off to another process.

Exit uses the configured application's main window. The companion selects it when the hold begins, checks it again at the hold threshold, brings it forward and sends a normal close request. See the instance selection rules below.

## Choosing which instance to close

- If the configured application's main window is active, the button closes that instance.
- If another application is active and only one instance of the configured app has a visible main window, the companion brings it forward and requests closure.
- If several instances are in the background, activate the intended one before holding the button.

The selected window is checked again before closing. Switching to another instance during the hold cancels the request. A modal dialog must be answered first. The application receives a normal close request, like clicking its window's close button; it handles saving itself.

## Windows startup and sleep

Toggle **Start with Windows** in the tray menu to register this executable for the current user's Windows sign-in. It uses the current-user Run registry key, does not need administrator access, and starts silently. Disable it before removing the utility; after moving the folder, enable it again from the new location. The checkbox is independent of `companion.ini`.

The tray status names a detected running application or DAW instead of suggesting that an unavailable action is ready. **Show status** includes the blocking processes, startup setting and config location.

On suspend, pending holds and foreground requests are cancelled. On resume, controller inventory and process checks run again and standby lighting is refreshed even if Windows retained the device handle. Existing Maschine/DAW guards and a one-hour pause still apply. A held button must be released before another action. The notification handler was tested with synthetic power messages; real sleep/hibernate recovery still needs hardware confirmation.

## Temporary pause

Choose **Pause for 1 hour** in the tray menu to stop companion launch/exit actions and standby lighting. Companion-owned standby lighting is cleared once if no native owner is detected; an active Maschine/plug-in panel is left untouched. The tray shows the local resume time. **Resume** ends the pause early. **Pause for 1 hour** is hidden during the pause and returns after resume or expiry.

Resume is silent and restores standby lighting only when the usual Maschine/DAW guards permit it. The pause expires by elapsed wall-clock time, including sleep, and applies to the current companion session; restarting the companion clears it. Release a held button before trying another action.

## Standby light

The standby LED defaults to **Dim**. Set `StandbyBrightness=Normal` or `StandbyBrightness=Bright` in `companion.ini`, then select **Reload config** from the tray menu. Omitting this setting keeps Dim. These are hardware brightness steps, not a percentage scale. The community Mikro MK3 light protocol updates the full LED state, so other lights are off in standby.

A short-lived shared write handle sends the LED report while Maschine and the configured target are closed. No HID input is read through that handle; input monitoring uses Windows Raw Input. LED writes are also blocked when Controller Editor or Komplete Kontrol is detected. Once the application is running, the companion leaves its lighting to the native software.

With `PauseWhenDawRunning=true`, when a newly started DAW is detected during companion-owned standby, the companion makes one final attempt to clear its standby lighting, then pauses controller actions and LED writes. The handoff is skipped if the host is already more than three seconds old, a Maschine module is loaded, another native owner is detected, or process inspection fails. It never repeatedly clears lighting during the DAW session. The defaults include Ableton Live, FL Studio, REAPER, Cubase, Nuendo, Studio One, Bitwig Studio, Pro Tools, and Cakewalk/Sonar. The `Ableton Live*` rule matches Live editions and versions, but excludes Ableton helper processes. This guard applies even if the DAW has no Maschine plug-in loaded. After the last matching DAW closes, standby lighting is sent again when Maschine and the other blockers are also closed. The companion does not restore a plug-in panel while its host is running. The final standby clear uses a full LED report; process/module checks are snapshots, so a plug-in loading simultaneously can still race with this handoff. A stale standby light may remain when the conservative checks skip clearing.

To add another host, find its executable name in Task Manager's Details tab, for example `DawProcesses=Ableton Live*;reaper`. Names are case-insensitive; the `.exe` suffix is optional. Only a trailing `*` is supported; paths and a global `*` are rejected. Use **Reload config** after editing. Hosts absent from this list cannot be detected as plug-in owners; add their process name or exit the companion during those sessions. If another service rejects or overwrites the light report, check the log; button-driven launch can still work.

To keep the companion active while a DAW runs, set `PauseWhenDawRunning=false` and choose **Reload config**. Your DAW list is retained for later use. This disables DAW-based launch/exit blocking and LED handoff; standalone/native-process guards still apply. With this option disabled, standalone Maschine and a plug-in can compete for the controller and lighting, as in the previously observed workflow.

## Default DAW process rules

| DAW | Process rule(s), without `.exe` | Evidence |
| --- | --- | --- |
| Ableton Live | `Ableton Live*` | Live 11 Lite and Live 12 Standard executables found locally. |
| FL Studio | `FL`, `FL64`, `FL (scaled)`, `FL64 (scaled)`, `FL (compatible memory)` | [Image-Line launcher documentation](https://support.image-line.com/action/knowledgebase?ans=526). |
| REAPER | `reaper` | [SWS extension build instructions](https://github.com/reaper-oss/sws/wiki/Building-the-SWS-Extension/eaf723bbfd959d91a8113260fd11943e3ec96bac). |
| Cubase / Nuendo | `Cubase*`, `Nuendo*` | [Steinberg executable names](https://helpcenter.steinberg.de/hc/en-us/articles/15279887297298-Graphical-User-Interface-errors-in-Cubase-13-Nuendo-13); prefix rules accommodate version suffixes. |
| Studio One | `Studio One` | [PreSonus support](https://support.presonus.com/hc/en-us/articles/29133689796109-Studio-One-Pro-7-Studio-One-Remote-not-connecting). |
| Bitwig Studio | `Bitwig Studio` | [DrivenByMoss installation instructions](https://github.com/git-moss/DrivenByMoss/wiki/Installation/4341232767480413cc875daa19b73f76cdcb0708). |
| Pro Tools | `ProTools` | [Avid Windows instructions](https://kb.avid.com/pkb/articles/en_US/Knowledge/Optimising-Processor-Affinity-Windows). |
| Cakewalk by BandLab | `Cakewalk` | [Executable observed in the vendor-hosted forum](https://discuss.cakewalk.com/profile/889-tay-zonday/); this is a community observation. |
| Cakewalk Sonar | `Sonar` | [Cakewalk configuration documentation](https://help.cakewalk.com/hc/en-us/articles/60845768676889-Configuration-Profiles-in-Cakewalk-Sonar). |

These are process-name presets, not a claim of tested controller/plug-in compatibility. Names can change between versions or be renamed. Verify Task Manager's Details tab if your DAW does not pause the companion. Prefixes may also match similarly named processes; remove unnecessary rules from your local configuration.

## Tested setup

Hardware behavior was confirmed on one Windows PC with Mikro MK3 and Maschine 3.7.1: dim standby light, launch, foreground activation, and normal exit. The Ableton workflow was also confirmed: DAW startup clears standby lighting and blocks standalone launch, the Maschine plug-in takes over the controller, and DAW exit restores standby lighting after a short delay. Other DAWs, controllers, and software versions have not been tested. Launch and close use the application configured in `Executable`. Closing checks its full executable path, not just its process name.

The companion keeps the controller in native mode and works with the official NI software. It does not change Maschine's EXE, scripts, drivers or firmware.

## Troubleshooting

- **No action or standby light:** use **Show status**. A hidden Maschine process still blocks launch; check Task Manager's Details tab. The companion never kills it.
- **Wrong executable path:** edit `companion.ini` and reload, or restart after correcting an initial configuration error.
- **Exit does nothing:** close any modal dialog manually, release the button, and hold again. If several instances of the configured application are open, activate the intended one and keep it active while holding the button. Check the log if foreground activation was denied.
- **Window stays behind other applications:** inspect `foreground` entries in the log. Windows can deny foreground activation.
- **LED is unavailable:** inspect `standby LED unavailable` entries and verify the official driver and connected device.

Logs are in `logs\companion-*.log`. They include local executable/device paths and process identifiers. Review logs before sharing them. A session log is truncated when it reaches 2 MiB; old session files are retained until you remove them. Logging failures do not stop controller handling.

## Developer tests

Run tests separately with:

```powershell
.\bin\CompanionTests.exe --self-test
```

Tests cover hold thresholds, one action per press, cancellation, duplicate input reports, configuration validation, full-path close matching, active-instance selection and ambiguous or blocked close targets. Hardware validation still requires a real controller.

## Related projects

Projects found in the protocol research and local source reviews.

### Controller protocols and libraries

- [Maschine.Api](https://github.com/panoramicdata/Maschine.Api) - .NET USB HID library for Maschine controllers.
- [maschine-mikro-mk3-driver](https://github.com/r00tman/maschine-mikro-mk3-driver) - Linux userspace MIDI driver for Mikro MK3.
- [maschine.rs](https://github.com/wrl/maschine.rs) - Rust code for communicating with Maschine hardware.
- [CABL](https://github.com/shaduzlabs/cabl) - controller abstraction library with Maschine support. Related forks: [Drachenkaetzchen](https://github.com/Drachenkaetzchen/cabl) and [styyle14](https://github.com/styyle14/cabl); the latter was downloaded for this research.
- [openAV-Ctlra](https://github.com/openAVproductions/openAV-Ctlra) - C library with controller drivers and HID decoding, including Maschine.
- [Encdr](https://github.com/RufusIbiza/Encdr) - Rust NI controller library with HID layouts and display protocols.

### NI protocol research

- [Rebellion](https://github.com/terminar/rebellion) - NI controller integration and IPC research.
- [NIProtocol](https://github.com/SamL98/NIProtocol) - macOS experiments reading Mikro MK2 input through NIHardwareAgent and sending input to Maschine 2.
- [Macchina](https://github.com/biappi/Macchina) - experiments reverse-engineering Maschine hardware communication.

### Related NI integrations

- [encdr-maschine-bitwig](https://github.com/RufusIbiza/encdr-maschine-bitwig) - Maschine MK3 integration for Bitwig using Encdr.
- [reaKontrol](https://github.com/brummbrum/reaKontrol) and [jcsteh's fork](https://github.com/jcsteh/reaKontrol) - Komplete Kontrol integration for REAPER, referenced in the NI integration research.
- [S4MK3-HID-MIDI-BRIDGE](https://github.com/jtoronto/S4MK3-HID-MIDI-BRIDGE) - HID-to-MIDI bridge for Traktor S4 MK3, useful as another NI hardware protocol example.

### Credits

Thanks to [maartenvantjonger](https://github.com/maartenvantjonger) for [Maschine Launcher](https://github.com/maartenvantjonger/maschinelauncher), a community tool that launches Maschine when a controller is connected.

This is an independent community utility and is not affiliated with Native Instruments. Locally built binaries are unsigned.

## License

[MIT](LICENSE).
