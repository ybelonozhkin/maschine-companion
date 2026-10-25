# Maschine Mikro MK3 Companion

Use the Maschine Mikro MK3 as a control surface for Ableton Live on Windows. Play drums and instruments, record MIDI, build patterns in the step sequencer, edit notes, browse sounds and launch clips from the controller.

Pads show track and clip colors, selected sounds and note activity. The OLED shows the parameter name and its value.

Version **0.4.0**. See the [changelog](CHANGELOG.md) for changes and the [roadmap](ROADMAP.md) for planned features.

## Ableton Live

Tested with **Live 12.4.6 and 12.4.7 Standard on Windows 11 x64**. The companion uses the Mikro in native mode and connects to a Live Remote Script. Enable the integration during setup.

| Feature | Controls |
| --- | --- |
| Play drums and instruments | Pad Mode; encoder changes note banks. |
| Play scales | Keyboard; arrows choose Root Note, Scale or Scale Mode; encoder changes the value. |
| Repeat notes | Hold Note Repeat, or pin it with Shift + Note Repeat; arrows choose Rate or Swing. |
| Play arpeggios | Note Repeat in Keyboard mode; note order, octaves, gate, swing and Latch. Arp Rhythm is beta. |
| Edit a pattern | Step; encoder changes pages, Select auditions sounds, Duplicate doubles the loop. |
| Launch clips | Pattern; 4-by-4 Session grid, encoder scrolls scenes, Shift + encoder scrolls track columns. |
| Launch scenes | Scene; four pads in the right column, encoder scrolls scenes. |
| Navigate Arrangement | Shift + Variation; Position and Markers pages. Shift + Scene switches Session / Arrangement views. |
| Select and edit notes | Events and the editing commands printed on the pads. |
| Load sounds | Browse; encoder navigates, press to expand or load, Favorites toggles the red Collection. |
| Adjust devices | Plug-in; arrows select parameters, Shift + arrows select devices. Encoder adjusts values; Shift gives fine control. |
| Control playback | Play, Stop, Rec, Restart, Tap and Follow; Shift accesses Loop and Metronome. |
| Select tracks (beta) | Group + arrows select tracks; Group + Shift + right creates a MIDI track. |
| Mix the selected track | Mute, Solo and Volume. Volume changes by 0.5 dB, or 0.1 dB with Shift; encoder press resets it to 0 dB. |
| Change expression | Fixed Vel / 16 Vel, Pitch and Mod on the Smart Strip. |

See [installation and requirements](GETTING-STARTED.md#ableton-live-installation) and the [full controls guide](ableton/README.md). Arm tracks manually for recording. When using Maschine as a plug-in, choose **Pause** in the tray or enable the [experimental automatic handoff](ableton/README.md#experimental-automatic-handoff).

## Download or build

The project is available as source and as a ready-made Windows x64 build:

- **Run it:** download `MaschineCompanion-<version>-win-x64.zip` from [Releases](https://github.com/ybelonozhkin/maschine-companion/releases) when available. Extract it and open `GETTING-STARTED.md` for requirements, installation and Live MIDI settings. The ZIP includes the app, its DLLs, the Live script and the controls guide.
- **Build it:** clone this repository or download the release's **Source code** archive. Follow the [build instructions](CONTRIBUTING.md#build-and-test). Source archives contain the build scripts and tests.

Development builds are under [Actions](https://github.com/ybelonozhkin/maschine-companion/actions/workflows/build.yml): open a successful run and download `windows-x64` from Artifacts. Extract that artifact to find the portable ZIP and its checksum. GitHub requires sign-in for artifact downloads. Release 0.2.0 predates the Live integration.

The ready-made build includes the MIDI bridge and Live script. Build tools are needed only when compiling the source. These builds are unsigned; Windows security policies may warn or block them. See [download requirements](GETTING-STARTED.md#unsigned-builds).

## Maschine launcher

The companion also lets you launch Maschine or request a normal exit by holding the Maschine button.

See [setup and controls](launcher/README.md) for installation, the tray menu and troubleshooting, or [configuration](launcher/CONFIGURATION.md) for `companion.ini` settings.

## Related projects

[Maschine Mikro Browser](https://github.com/ybelonozhkin/maschine-mikro-browser) adds browser controls, effect management, and save-prompt selection through a separate Maschine patch.

[Maschine Launcher](https://github.com/maartenvantjonger/maschinelauncher/pulls) is another community project for launching Maschine when a controller connects.

## License

[MIT](LICENSE). Dependency licenses and controller protocol acknowledgements are in [Third-party notices](THIRD-PARTY-NOTICES.md). Downloads include these notices and the full dependency licenses.

This project is independent of Native Instruments and Ableton.
