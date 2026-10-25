# Changelog

All notable changes are documented here, using [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Experimental controller handoff between the Live integration and Maschine VSTs, enabled with `EnableNativeHandoff`. Select between multiple Maschine instances manually in their Controller menus.

## [0.3.0] - 2026-10-24

### Added

- Optional Ableton Live integration through a Live Remote Script and a virtual MIDI port, using the Mikro MK3 in native mode.
- Pad Mode with 16-note banks, Drum Rack grid synchronization, track colors, empty-pad indication, selected-note lighting and note activity flashes.
- Keyboard mode with Live's root note and scale, highlighted roots and separate note ranges for Pad Mode and Keyboard.
- Step sequencer with 16 steps per page, sound selection while holding Select, loop duplication and a Smart Strip overview. The first step in an empty Session slot creates and launches a MIDI clip. Manually recorded notes are recognized at their nearest grid point without moving them.
- Pattern mode with a 4-by-4 Session clip grid, clip colors, playback states and a matching frame in Live. Select chooses a destination for recording or Step; Shift exposes four track Stop pads.
- Scene mode for launching four Session scenes from the right pad column. Pattern and Scene share their navigation position.
- Events mode for selecting notes by pitch or time range, with Erase and the editing commands printed on the pads: Undo, Redo, Quantize, Quantize 50%, Nudge, Semitone and Octave.
- Live transport controls, recording that follows Session or Arrangement focus, Restart, Loop, Tap Tempo, Metronome, Follow and held Grid adjustment. Track arming remains manual.
- Note Repeat with tempo synchronization, straight and triplet rates, swing, pressure-controlled velocity and Shift + Note Repeat to pin the mode.
- Keyboard arpeggiator with five note orders, octave range, gate, swing and Latch. Arp keeps each note's initial strike velocity.
- **Beta: Arp Rhythm** with 64 eight-step patterns and a pattern diagram on the display.
- **Beta: track navigation** with Group + arrows. Group + Shift + right creates and selects a MIDI track; holding Group displays the selected track's number and name.
- Browser navigation, folder and preset browsing, plug-in loading and the red Favorites Collection. Shift + encoder jumps directly by six items.
- Volume in 0.5 dB steps, 0.1 dB with Shift, and encoder press to reset to 0 dB. Tempo encoder controls, Fixed Vel, 16 Vel, and Smart Strip pitch bend and modulation.
- Plug-in parameter control: arrows select parameters, Shift + arrows select devices, and Shift + encoder gives fine adjustment. The display shows Live's formatted values.
- Arrangement navigation with Position and Markers pages. Position moves by one bar, or four with Shift; Shift + Scene switches Session and Arrangement views while preserving the controller mode.
- Two-line parameter display with a bold heading and a thin value line. Inverse fields identify the encoder target; long names retain their beginning and end.
- Indefinite Pause in the tray for handing the controller to another application. Resume also clears the pause caused by switching to MIDI mode after returning to native mode.
- Tray Vegas mode for a standby animation while its checkbox is enabled. Starting Live or Maschine clears the checkbox.
- Saved **Dim** checkbox in the tray for minimum OLED contrast.
- Build and installation scripts, tests and a controls guide for the Live integration.
- GitHub Actions builds for Windows x64, with downloadable binaries, file checksums and tag-triggered draft releases.

## [0.2.0] - 2026-10-12

### Added

- OLED press feedback: an underscore in the first row, cleared before launch or after an early release.
- Log entries for press-to-launch-command time, hold duration, and overhead.
- Windows lock/unlock notifications and input-desktop checks.

### Changed

- Pause actions and clear standby lighting when Windows is locked or the input desktop is unavailable.
- Recheck devices and applications after unlock before restoring standby lighting.

## [0.1.0] - 2026-10-11

### Added

- Initial release.
