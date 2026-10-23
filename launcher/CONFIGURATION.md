# Configuration

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

Live integration settings are `EnableAbleton` (default `false`), `FixedVelocity` (1-127, default 127), and optional `MidiActivityInputs`. See the [Live guide](../ableton/README.md) before enabling them. When enabled and connected to its script, the integration handles Live instead of leaving the controller idle under DAW blocking.

For setup, controller controls and the tray menu, see the [launcher guide](README.md).
