# Basic MIDI mode

Basic mode turns Mikro into a MIDI controller for Live or another software instrument. It needs the companion and Windows MIDI Services, but no Live Remote Script. This first version supports one connected Mikro MK3.

## Setup

1. Keep Mikro in native mode and start the companion.
2. In the tray, choose **Mikro MK3 > Mode > Basic mode**.
3. In your music application, enable **Mikro Companion** as a MIDI input. In Live, enable Track for this input and leave Track off for **Mikro Companion Send**.
4. Choose a MIDI instrument track, set its input to Mikro Companion and the desired channel, then arm it. Load a Drum Rack or another instrument.

**Mikro MK3 > Basic MIDI channel** selects channels 1–16. Pad mode starts at MIDI note 36, matching Live's default Drum Rack bank. Keyboard has its own local root, scale and range. The receiving application controls sound, monitoring and recording.

The **Mikro MK3** submenu groups Mode, MIDI channel, clock, Dim, Vegas and local pattern actions. Its title includes the serial number once identified. Device settings are saved locally in `devices.ini`, including clock source/input, internal tempo, display contrast and Fixed Velocity. The file is not part of the distribution. Local patterns stay in memory until the companion exits; save a MIDI file to keep your work.

**Detect** in the main tray menu shows the serial in large text while the companion controls Mikro. Any physical action restores the usual screen and still performs its normal function. Detect does not take control away from Maschine.

## Controls

| Control | Basic action |
| --- | --- |
| Pad Mode | Play a 16-note bank; turn the encoder to change banks; press it to reset the range. |
| Keyboard | Play a compact scale layout. Arrows choose Root Note, Scale or Scale Mode; turn the encoder to adjust. Shift + encoder on Root Note changes octaves. Encoder press resets the starting octave. |
| Fixed Vel / Shift + Fixed Vel | Fixed velocity / 16 velocity levels for the last played note. |
| Pitch / Mod | Assign the Smart Strip to pitch bend or modulation. |
| Note Repeat in Pad Mode | Hold for repeats; Shift + Note Repeat pins it. Encoder changes Rate or Swing, selected with arrows. Pressure changes repeat velocity. |
| Note Repeat in Keyboard | Toggle Arp; arrows select a parameter and encoder changes it. Shift + Note Repeat toggles Latch. Arp retains the strike velocity. |
| Tempo | Show clock source and BPM; encoder changes internal tempo in whole BPM. Tap sets internal tempo. |
| Step | Toggle local Step editing; encoder changes pages. Hold Select to audition/select a sound, change its bank/root with the encoder, or reset with encoder press. |
| Play / Stop / Restart / Rec | Play, stop, restart or toggle recording into the local pattern. Ordinary pad playing works while stopped. |
| Duplicate in Step | Double the pattern, up to 64 bars of 4/4. |
| Erase in Step | Remove notes of the selected pitch. |
| Shift + Grid | Hold and turn the encoder to choose the local grid. |
| Shift + pads 1 / 2 | Undo / Redo local pattern edits. |
| Shift + pads 5 / 6 | Quantize / Quantize 50% for the local pattern. |
| Shift + pad 9 | Clear notes of the selected pitch. |

There are 13 local scales: Major, Minor, Dorian, Phrygian, Lydian, Mixolydian, Locrian, Harmonic Minor, Melodic Minor, Major and Minor Pentatonic, Blues and Chromatic. The scale selection is independent of Live. Lighting shows the note layout and local activity; occupied drum cells and preset names are not available in Basic.

## Aftertouch

Choose **Mikro MK3 > Aftertouch > Channel** or **Polyphonic** to send held-pad pressure to your instrument. Off is the default. Channel applies the strongest held pad's pressure to the whole MIDI channel; Polyphonic sends pressure separately for each note. The instrument or preset needs a pressure assignment, such as filter cutoff or vibrato.

This experimental feature works for ordinary Pad/Keyboard playing and Step Select audition, including Fixed Vel and 16 Vel. Note Repeat keeps its pressure-to-velocity behavior, and Arp keeps its strike velocity. Generated playback does not create pressure curves; local Step recording and MIDI export currently store notes without them.

## Local pattern

Step edits one looping pattern. The first step in an empty pattern starts playback when clock is available. Rec records pad notes, including their velocity and held duration. Notes played slightly off-grid are recognized at their nearest step without being moved.

The Smart Strip shows the whole pattern, with the page being edited in white and a bright playback marker.

Use **Mikro MK3 > Save MIDI file...** to export one full cycle of all note parts, with tempo and 4/4 time signature. The file contains notes, not the instrument or its preset. **Reset step sequencer...** stops playback and clears the pattern; controller settings remain. Save before exiting or replacing the local session.

## Clock

**Mikro MK3 > Basic clock > Internal** is the default. Tempo and Tap control the companion's clock; Note Repeat and Arp work even while Step is stopped.

For synchronization from Live:

1. Choose **Mikro MK3 > Basic clock > External MIDI**.
2. Leave **External input** set to **Mikro Companion Send**.
3. In Live's MIDI output settings, enable **Sync** for **Mikro Companion**. This is the return direction of the same virtual cable.
4. Start Live's transport. External MIDI Start/Continue/Stop control local Step playback.

You can instead select another MIDI clock input in the tray. The Tempo screen shows External and received BPM, or Waiting. Without clock, repeats and sequencing wait; ordinary notes still work. Clock loss releases generated notes and never silently switches to internal tempo. Transmission of clock to the receiving instrument is not enabled by this setting.

## Control surface and launcher

Choose **Mikro MK3 > Mode > Control surface** to use the Live Remote Script. Until a working script connection arrives, the controller remains in Basic and displays Waiting for Live. When Live is ready, held notes, a latched Arp or an active local pattern defer the switch; finish playing or press Stop. Explicit Basic mode stays Basic when Live starts.

**Mode > Launcher standby** restores the original dim Maschine button and hold-to-launch behavior. Pause, Windows lock and native Maschine ownership still take priority. If another application uses the controller, use Pause; Basic cannot detect Maschine plug-ins in every host.
