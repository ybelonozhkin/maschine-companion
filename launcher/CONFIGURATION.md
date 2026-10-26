# Configuration

Edit `companion.ini` in the project folder, then choose **Reload config**. Changes to `EnableAbleton`, `EnableNativeHandoff` and `MidiActivityInputs` require a companion restart. Windows startup is controlled separately through the tray menu.

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
| `DisplayDim` | Minimum OLED contrast when `true`; normal contrast when `false` (default). Also controlled by **Dim** in the tray. |
| `EnableClose` | Allow normal close requests for the configured application. |
| `CloseHoldMilliseconds` | Exit hold time: 1000-10000 ms. |
| `PauseWhenDawRunning` | Pause for listed DAWs when `true` (default). Set `false` to ignore the list. |
| `DawProcesses` | Process names separated by `;`. A trailing `*` matches a prefix. Names are case-insensitive; `.exe` is optional. An empty list disables DAW blocking. |

To add a DAW, copy its executable name from Task Manager's **Details** tab into `DawProcesses`.

For Live, set `EnableAbleton=true`. `FixedVelocity` sets Fixed Vel (1-127, default 127); `MidiActivityInputs` lists other controllers whose strikes should flash the pads. See the [Live guide](../ableton/README.md) for setup.

For setup, controller controls and the tray menu, see the [launcher guide](README.md).
