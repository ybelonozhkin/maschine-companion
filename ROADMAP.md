# Development plan

Basic MIDI mode, local sequencing and clock selection are available in 0.5.0 for one Mikro MK3. See the [Basic guide](basic/README.md). The proposals below are not implemented yet.

## Basic mode: next steps

Add an on-controller settings screen for MIDI channel and clock. The entry button remains undecided; Plug-in is a candidate in Basic, while keeping its Live device function in Control surface.

For multiple Mikro units, remember each device's profile, MIDI channel, layout, clock and local pattern independently. Allow different units to share a channel, or route them to separate instruments on channels 1–16. Validate device identity across USB ports and reconnection before enabling simultaneous operation. Consider separate virtual ports where channel filtering is insufficient.

## Two Mikro controllers

Support two Mikro MK3 units with independent modes. Start with **Pad on A and Step on B**, both working on the same Live track, so playing and step editing do not require switching modes.

Also explore **a fixed instrument track on A and Pattern/Scene on B**. Give A a dedicated MIDI route so selecting tracks or launching clips with B does not redirect its notes. If the fixed track is deleted, stop A's notes rather than silently choosing another instrument. Clips launched on that same track naturally play the same instrument alongside A.

Validate separate device identity, ownership, input and feedback before implementation. Keep the scope to these two workflows; a general role-assignment system is not planned.

Extend the global **Detect** command to all companion-owned units: each OLED shows its own serial in large text until an action on that unit. The action dismisses only that unit's label and is processed normally. Single-device identification is implemented; simultaneous identification needs multiple-device support.

## Copy and Paste: notes and clips

Activate the controller's labeled **Copy** and **Paste** commands for context-specific editing. In Events, copy selected notes and paste them at an explicitly chosen position, preserving their relative timing, pitch, duration and velocity. Define destination selection before implementation so pasting does not depend on accidental computer keyboard focus.

In Pattern, copy the selected clip and paste it into an explicitly selected compatible Session slot, including an empty slot. Keep the destination selected for further editing. Preserve the existing Duplicate action for making an adjacent copy; Copy/Paste allows choosing the destination separately. Require confirmation before replacing an occupied clip, and support Undo.

Keep note, clip and property buffers distinct. A Copy/Paste command must clearly follow the current editing context; property copying needs a separate explicit assignment within parameter editing.

## Events: note properties

Extend Events with editing of selected notes' velocity and length. Keep note selection and parameter editing as separate states so the encoder can still navigate notes and extend a selection. The exact controls remain to be agreed.

Add **Copy properties** and **Paste properties** for velocity and length, with probability included if its editing is added later. Preserve the destination notes' pitch and position. Match source and destination notes by time, with simultaneous notes ordered by ascending pitch. Apply only as many property sets as were copied: five sources pasted onto three targets change three notes; three sources pasted onto five targets leave the last two unchanged. A single source changes only the first target.

Paste requires an explicit action and a nonempty target selection. Keep the copied properties available for repeated pastes, and make each paste a single Undo operation. Copy/Paste button assignments remain to be defined.

## Arp rhythms: Launchkey observation

Review Launchkey MK4's Arp rhythms through normal controller use: turn its rhythm control and record the user's descriptions of the display and audible pattern. Use these observations to decide which rhythmic choices and display feedback would be useful in Companion, and curate the current bank of 64 patterns. The final set and controls remain to be agreed.

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
