# Development plan

Version 0.3.0 covers playing instruments, sequencing and editing MIDI, launching clips, browsing sounds and controlling transport. The proposals below are not implemented yet.

## Chords

Add a dedicated **Chords** mode for playing chords from the pads. The chord layout and controls still need to be defined.

## Group: select tracks with pads

Hold **Group** to show up to 16 Live tracks in their colors. Press a pad to select a track, then release Group to return to the previous playing mode. The encoder would move between banks of tracks.

Here, a pad represents a Live track. Drum Rack sounds remain inside their instrument track; Live Group Tracks keep their usual meaning.

## Drum Rack Mute and Solo

Holding **Mute** or **Solo** while pressing a pad could control that Drum Rack sound. A press and release without a pad would retain the current track-level action. This needs a gesture that avoids toggling the track before the user selects a sound.

## Swing

Use **Swing** and the encoder to adjust Live's swing amount. Define how it works with Quantize and the separate Note Repeat and Arp swing settings, while preserving note selection in Events.

## Humanize

Use **Variation** to humanize selected MIDI notes, or the open clip when no notes are selected. The encoder would set the amount; a press would apply it, with Undo available. Investigate access to Live's native Humanize controls before choosing the implementation.

## Compatibility testing

Test the Live integration on more Windows 11 systems, including MIDI port creation, sleep recovery and switching between native and MIDI modes. Extend support based on those results.

## Scope

New functions should follow the labels on the controller and preserve the existing playing modes. Maschine project saving belongs to the separate [Maschine Mikro Browser](https://github.com/ybelonozhkin/maschine-mikro-browser) project.
