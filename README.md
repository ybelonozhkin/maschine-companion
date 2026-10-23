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

## Maschine launcher

See [setup and controls](launcher/README.md) for installation, the tray menu and troubleshooting, or [configuration](launcher/CONFIGURATION.md) for `companion.ini` settings.

## Related projects

[Maschine Mikro Browser](https://github.com/ybelonozhkin/maschine-mikro-browser) adds browser controls, effect management, and save-prompt selection through a separate Maschine patch.

[Maschine Launcher](https://github.com/maartenvantjonger/maschinelauncher/pulls) is another community project for launching Maschine when a controller connects.

## License

[MIT](LICENSE). Dependency licenses and controller protocol acknowledgements are in [Third-party notices](THIRD-PARTY-NOTICES.md). Downloads include these notices and the full dependency licenses.

This project is independent of Native Instruments and Ableton.
