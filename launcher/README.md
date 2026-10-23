# Maschine launcher

For installation, see [Getting started](../GETTING-STARTED.md). For source builds, see [Build and test](../CONTRIBUTING.md#build-and-test). Launcher settings are in [Configuration](CONFIGURATION.md).

## Controller controls

| Action | Result |
| --- | --- |
| Hold the Maschine button for 1.2 seconds | Launch Maschine and bring its window forward. |
| Hold it for 2 seconds while Maschine is running | Bring its window forward and request a normal exit. |
| Release before the hold time | Cancel the action. |

The standby light indicates that launching is available. An underscore on the OLED gives visual feedback while you hold the button to launch.

You can change the target application and hold times in `companion.ini`. If several instances are open, activate the one you want to close. Save prompts and other dialogs need a response on the computer.

## Tray menu

- **Pause for 1 hour** pauses the companion for an hour.
- **Pause / Resume** pauses until you resume. Restarting the companion also clears a manual pause.
- **Vegas mode** animates the lights while the controller is idle. It switches off when Live or Maschine starts, or when the companion restarts.
- **Show status** explains whether the companion is ready or paused.
- **Reload config** applies changes to `companion.ini`; invalid settings leave the previous config active.
- **Start with Windows** enables startup for your account. Disable it before moving or removing the app, then enable it again from its new location.
- **Open log folder** opens the session logs.
- **Exit** closes the companion.

## Automatic pause

By default, a running DAW pauses the launcher. Closing the DAW restores standby lighting. Configure this through `PauseWhenDawRunning` and `DawProcesses`.

Windows lock and sleep also pause controller actions. The companion checks its state again after unlock or wake. Real sleep and hibernate recovery still need hardware testing.

Before loading Maschine as a plug-in, choose **Pause** manually. Choose **Resume** after releasing the controller from Maschine.

## Troubleshooting

- **The button does nothing:** check **Show status**. Maschine may still be running in the background. Wait 15 seconds between launch attempts.
- **Exit does nothing:** answer any open dialog, release the button and try again. If several instances are open, activate the intended one first.
- **Unexpected lighting:** check the DAW process list, or pause the companion before using another controller application. Controller Editor and Komplete Kontrol also block standby lighting.
- **Controls stop after switching to MIDI mode:** return to native mode and choose **Resume** in the tray.
- **Missing screen or LED feedback:** check `logs\companion-*.log` for errors.

Review logs for local paths before sharing them.
