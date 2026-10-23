# Maschine launcher

For a downloaded build, start with [Getting started](../GETTING-STARTED.md). This guide covers the controller controls, tray menu and launcher behavior. See [Configuration](CONFIGURATION.md) for `companion.ini` settings.

## Setup from source

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

## Controller controls

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

## Troubleshooting and limitations

- **The button does nothing:** check **Show status**. An existing target or Maschine process blocks launch even if it has no visible window. Each launch attempt also starts a 15-second cooldown.
- **Exit does nothing:** answer any modal dialog, release the button, and try again. If several instances are open, activate the intended one. Windows may deny foreground activation.
- **The standby light remains on when a DAW opens:** clearing is skipped if the host is already more than three seconds old, a Maschine module is loaded, or inspection fails. This avoids writing when control may have passed to the plug-in.
- **Unexpected plug-in lighting:** enable DAW blocking and check the process list. Hosts absent from the list are not covered. Disabling DAW blocking lets standalone Maschine and its plug-in compete for the controller.
- **Screen or LED feedback is missing:** check the log for HID errors. A rejected or pending write can prevent display cleanup or delay an action.

LED updates write the whole panel state. Process and module checks cannot make the handoff atomic: a plug-in can start between the check and the write. The Ableton handoff was tested on one PC; other DAWs and driver versions need further testing. Controller Editor and Komplete Kontrol also block standby lighting when detected.

Logs are stored in `logs\companion-*.log`. They include process identifiers and local paths; review them before sharing. A session log is truncated at 2 MiB. Old session files remain until you remove them.

The log records the time from button press to the launch command. Buttons use Windows Raw Input events; process and desktop checks run every 500 ms, and device inventory is checked every second.

After building from source, run the tests separately with:

```powershell
.\bin\CompanionTests.exe --self-test
```

