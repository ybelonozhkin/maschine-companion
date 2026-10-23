# Maschine Mikro MK3 Companion

A Windows tray app for the Maschine Mikro MK3: launch and close Maschine, or use the controller to play and edit music in Ableton Live.

The <img src="assets/maschine-button.svg" alt="Maschine" width="20" height="20"> button lights up when the companion is ready. Hold it to launch Maschine. Hold it again to bring the open window forward and request a normal exit.

The optional Live integration adds drum and scale layouts, a step sequencer, note editing, Note Repeat, browser navigation and transport controls. Pads show track colors, selected sounds and note activity. The OLED shows the parameter name above its value. Hold Group to see the selected track.

Development since **0.2.0** adds the Live integration and an indefinite pause command. Arp Rhythm and track navigation are beta features. See the [changelog](CHANGELOG.md) for changes since 0.2.0 and the [development plan](ROADMAP.md) for proposed additions.

## Download or build

The project is available as source and as a ready-made Windows x64 build:

- **Run it:** download `MaschineCompanion-<version>-win-x64.zip` from [Releases](https://github.com/ybelonozhkin/maschine-companion/releases) when available. Extract it and open `GETTING-STARTED.md` for requirements, installation and Live MIDI settings. The ZIP includes the app, its DLLs, the Live script and the controls guide.
- **Build it:** clone this repository or download the release's **Source code** archive. Follow the [build instructions](CONTRIBUTING.md#build-and-test). Source archives contain the build scripts and tests.

Development builds are under [Actions](https://github.com/ybelonozhkin/maschine-companion/actions/workflows/build.yml): open a successful run and download `windows-x64` from Artifacts. Extract that artifact to find the portable ZIP and its checksum. GitHub requires sign-in for artifact downloads. Release 0.2.0 predates the Live integration.

The ready-made build includes the MIDI bridge and Live script. Build tools are needed only when compiling the source. These builds are unsigned; Windows security policies may warn or block them. See [download requirements](GETTING-STARTED.md#unsigned-builds).

To compile locally, use the launcher instructions below or the [full Live build instructions](ableton/README.md#setup). [Contributing](CONTRIBUTING.md) describes tests and the GitHub build process.

## Ableton Live

The integration is opt-in and was tested with **Live 12.4.6 Standard on Windows 11 x64**. It uses the Mikro in native mode and installs a Live Remote Script.

| Feature | Controls |
| --- | --- |
| Play drums and instruments | Pad Mode; encoder changes note banks. |
| Play scales | Keyboard; arrows choose Root Note, Scale or Scale Mode; encoder changes the value. |
| Repeat notes | Hold Note Repeat; arrows choose Rate or Swing, encoder changes the value. |
| Edit a pattern | Step; encoder changes pages, Select auditions sounds, Duplicate doubles the loop. |
| Launch clips | Pattern; 4-by-4 Session grid, encoder scrolls scenes, Shift + encoder scrolls track columns. |
| Launch scenes | Scene; four pads in the right column, encoder scrolls scenes. |
| Select and edit notes | Events and the editing commands printed on the pads. |
| Load sounds | Browse; encoder navigates, press to expand or load, Favorites toggles the red Collection. |
| Control playback | Play, Stop, Rec, Restart, Tap and Follow; Shift accesses Loop and Metronome. |
| Select and mix tracks (navigation: beta) | Group + arrows select tracks; Group + Shift + right creates a MIDI track. Mute, Solo and Volume affect the selected track. |
| Change expression | Fixed Vel / 16 Vel, Pitch and Mod on the Smart Strip. |

Start with the [Live setup and controls](ableton/README.md#setup). Live integration requires Windows MIDI Services support. Compiling it from source also requires C++ tools. Track arming remains manual. Before loading Maschine as a plug-in, choose **Pause** in the tray; choose **Resume** after releasing the controller from Maschine.

## Maschine launcher setup

You need Windows 11 x64, the official Mikro MK3 driver, Maschine, and the Windows .NET Framework compiler. The tested setup uses Maschine 3.7.1. Other controllers are not supported.

1. Clone or download this repository into a writable folder.
2. Open Windows PowerShell in that folder and build:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Companion.ps1
   ```

3. Edit `companion.ini` if Maschine is installed elsewhere. The build creates this file from `companion.example.ini` when it is missing.
4. Run `Start-Companion.cmd`. The app starts silently in the system tray. Its icon may be under the tray's hidden-icons arrow.
5. Connect the Mikro MK3. It can also be connected before the companion starts.

The build runs the tests and creates `bin\MaschineCompanion.exe`. It uses .NET Framework 4.x; no .NET SDK or NuGet packages are needed. Exit the companion before rebuilding.

## Maschine launcher controls

| Action | Result |
| --- | --- |
| Hold the Maschine button for 1.2 seconds | Launch the configured application and bring its window forward. |
| Hold it for 2 seconds while the application is running | Bring its main window forward and send a normal close request. |
| Release before the hold time | Cancel the action. |

These are the default hold times. Release the button before another action; keeping it held does not repeat the command.

For closure, the companion prefers the configured application's active main window. If another app is active, it selects a single visible instance in the background. If several instances are open, activate the one you want to close first. Switching to another instance during the hold cancels the request. Existing modal dialogs must be answered manually. The companion never forces a process to end.

During a launch press, `_` appears at the left edge of the first OLED row. It clears before the launch command, or when you release early. The rest of the screen is blank. Blocked presses and exit requests do not draw the marker.

Check **Vegas mode** in the tray menu to animate the controller in launcher standby. One random button or pad lights at maximum brightness, fades through its available levels every half-second, then waits a random 2-7 seconds before the next light. Pads use random colors. The Maschine standby button keeps its configured brightness; the Smart Strip stays off. Controller input interrupts the animation until released. Unchecking restores the standby light. The check clears when Ableton Live or Maschine starts, or when the companion restarts. Animation pauses while another application owns the controller, Windows is locked, or the companion is paused or sleeping. Vegas runs only while checked; inactivity never enables it.

## Tray menu

- **Pause for 1 hour** disables companion actions and clears its standby light when no blocking application is detected. It is hidden while the pause is active.
- **Pause** disables companion actions until you choose **Resume**. Both pause commands are hidden while paused.
- **Resume** ends a manual pause. A timed pause also ends after an hour, including time spent asleep. Restarting the companion clears manual pauses. After switching to MIDI mode, return the controller to native mode before choosing Resume.
- **Show status** shows why the companion is ready or paused, along with the config path and Windows startup setting.
- **Reload config** applies changes to `companion.ini`. A failed reload keeps the previous settings. Release a held button before trying again.
- **Start with Windows** enables startup for your Windows account. Disable it before removing the app; enable it again after moving the folder.
- **Open log folder** opens the session logs.
- **Exit** closes the companion.

Starting a second copy exits immediately. Normal startup, pause, and resume do not show notifications. Errors that prevent startup, launch, config reload, or startup registration are shown on the computer.

## DAWs, Windows lock, and sleep

By default, the companion pauses while a DAW in `DawProcesses` is running, even if no Maschine plug-in is loaded. When it detects a newly started DAW, it attempts to turn off its standby light once. It then leaves the controller to the DAW and plug-in. After the DAW closes, the standby light returns if no other blocker remains.

Windows lock disables companion actions and clears its standby light. Unlock triggers a fresh check of the controller and running applications. The light stays off if a DAW or manual pause still blocks operation. An unavailable input desktop, such as a secure UAC prompt, also pauses the companion.

After sleep, the companion cancels unfinished holds, checks the device again, and refreshes standby lighting. The sleep handler was tested with simulated notifications; real sleep and hibernate recovery still need hardware testing.

## Configuration

Edit `companion.ini` in the project folder, then choose **Reload config**. Windows startup is controlled separately through the tray menu.

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
| `Executable` | Absolute path to an existing `.exe`. Environment variables are accepted. Use the application itself, rather than a separate launcher. |
| `Arguments` | Optional command-line arguments. |
| `HoldMilliseconds` | Launch hold time: 500-10000 ms. |
| `StandbyLed` | Enable or disable the standby light and Vegas animation. |
| `StandbyBrightness` | `Dim` (default), `Normal`, or `Bright`. These are hardware levels, not percentages. |
| `EnableClose` | Allow normal close requests for the configured application. |
| `CloseHoldMilliseconds` | Exit hold time: 1000-10000 ms. |
| `PauseWhenDawRunning` | Pause for listed DAWs when `true` (default). Set `false` to ignore the list. |
| `DawProcesses` | Process names separated by `;`. A trailing `*` matches a prefix. Names are case-insensitive; `.exe` is optional. An empty list disables DAW blocking. |

The defaults cover Ableton Live, FL Studio, REAPER, Cubase, Nuendo, Studio One, Bitwig Studio, Pro Tools, and Cakewalk/Sonar. To add another host, copy its executable name from Task Manager's **Details** tab. These presets detect processes; they are not a claim that every host has been tested with the controller.

Live integration settings are `EnableAbleton` (default `false`), `FixedVelocity` (1-127, default 127), and optional `MidiActivityInputs`. See the [Live guide](ableton/README.md) before enabling them. When enabled and connected to its script, the integration handles Live instead of leaving the controller idle under DAW blocking.

## Troubleshooting and limitations

- **The button does nothing:** check **Show status**. An existing target or Maschine process blocks launch even if it has no visible window. Each launch attempt also starts a 15-second cooldown.
- **Exit does nothing:** answer any modal dialog, release the button, and try again. If several instances are open, activate the intended one. Windows may deny foreground activation.
- **The standby light remains on when a DAW opens:** clearing is skipped if the host is already more than three seconds old, a Maschine module is loaded, or inspection fails. This avoids writing when control may have passed to the plug-in.
- **Unexpected plug-in lighting:** enable DAW blocking and check the process list. Hosts absent from the list are not covered. Disabling DAW blocking lets standalone Maschine and its plug-in compete for the controller.
- **Screen or LED feedback is missing:** check the log for HID errors. A rejected or pending write can prevent display cleanup or delay an action.

LED updates write the whole panel state. Process and module checks cannot make the handoff atomic: a plug-in can start between the check and the write. The Ableton handoff was tested on one PC; other DAWs and driver versions need further testing. Controller Editor and Komplete Kontrol also block standby lighting when detected.

Logs are stored in `logs\companion-*.log`. They include process identifiers and local paths; review them before sharing. A session log is truncated at 2 MiB. Old session files remain until you remove them.

The log records the time from button press to the launch command. Buttons use Windows Raw Input events; process and desktop checks run every 500 ms, and device inventory is checked every second.

Run the tests separately with:

```powershell
.\bin\CompanionTests.exe --self-test
```

## Related projects

[Maschine Mikro Browser](https://github.com/ybelonozhkin/maschine-mikro-browser) adds browser controls, effect management, and save-prompt selection through a separate Maschine patch.

[Maschine Launcher](https://github.com/maartenvantjonger/maschinelauncher/pulls) is another community project for launching Maschine when a controller connects.

## License

[MIT](LICENSE). Dependency licenses and controller protocol acknowledgements are in [Third-party notices](THIRD-PARTY-NOTICES.md). Downloads include these notices and the full dependency licenses.

This project is independent of Native Instruments and Ableton.
