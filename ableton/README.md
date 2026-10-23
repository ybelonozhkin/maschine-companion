# Ableton Live integration

Use the Mikro MK3 in native mode to play instruments, edit MIDI clips and browse sounds. Tested with Live 12.4.6 Standard on Windows 11 x64.

## Setup

You need the official Mikro MK3 driver and Windows MIDI Services with loopback support. To compile from source, you also need Visual Studio C++ Build Tools (Desktop development with C++) and Windows SDK 10.0.26100.0. The build downloads a pinned Windows MIDI Services SDK; runtime port creation also requires the Windows service. Compatibility with other Windows builds has not been verified.

For a downloaded build, follow [Getting started](../GETTING-STARTED.md) and continue at step 4 below. The first three steps are for building and installing from source.

Close Live and exit the companion before building or updating. Start the companion before opening Live so its MIDI port is available.

1. Clone or download this repository into a writable folder. Open Windows PowerShell there and build with `powershell -NoProfile -ExecutionPolicy Bypass -File .\Build-Ableton.ps1`.
2. Install the Live script with `powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-AbletonScript.ps1`. Use `-UserLibrary` to specify another library location.
3. Set `EnableAbleton=true` in companion.ini and run `Start-Companion.cmd`. Leave the controller in native mode.
4. Start Live. In MIDI preferences, select **MikroCompanion** as a control surface, **Mikro Companion** as its Input, and **None** as its Output.
5. Enable **Track** for the **Mikro Companion** MIDI input. Select this port as the instrument track's MIDI source. Leave other controllers assigned to their existing surfaces.
6. Set the instrument track's monitoring and Arm state in Live. All 16 pads are available regardless of the loaded instrument.
7. Arm the tracks you want to record. In Session View, choose the scene or clip and press Rec. In Arrangement View, place the cursor and use Rec and Play to record there. Rec toggles recording without moving the cursor or starting playback in Arrangement. Its light follows the recording state of the focused view.

The MIDI input named **Maschine Mikro MK3** belongs to NI's independent MIDI mode. This integration uses the separate **Mikro Companion** port and does not change NI templates. **Mikro Companion Send** is the internal side of the cable; leave it unassigned in Live. Keep Track and Sync disabled for the Mikro Companion output; instrument tracks keep their normal output routing.

## Pause before using Maschine

Choose **Pause** in the companion tray menu before loading Maschine VST or switching to another controller application. Pause releases generated notes and stops panel updates. It stays paused until you choose **Resume**. Close/remove Maschine, return the Mikro to native mode, then resume.

**Pause for 1 hour** resumes automatically; use the indefinite **Pause** command for Maschine sessions. Starting Maschine standalone also blocks the native integration. Automatic detection of Maschine plug-in ownership is not implemented.

## OLED display

A Mikro Companion startup screen appears for two seconds when the integration first connects to Live or the controller reconnects over USB while Live is running. It then gives way to the current mode. Pause and Resume skip the startup screen.

Both rows use the same text height: a bold parameter name above a thin value. Inverse values identify the encoder target. The arrow buttons light when another parameter page is available; selection stops at the ends.

Hold **Group** to show the selected track's number and name, including renames. Group + arrows select tracks; releasing Group restores the previous parameter screen. Long names keep their beginning and end, separated by `..`.

Keyboard has Root Note, Scale and Scale Mode pages. When Live's Scale Mode is off, the Scale heading reads SCALE (OFF), while its value still shows the chosen scale. Step shows its page counter; holding Select shows the audition note or bank. In Pattern, the encoder scrolls scenes and Shift + encoder scrolls track columns. Scene shows its scene range.

## Playing and transport

The 16 Mikro MK3 pads send MIDI notes with velocity through the **Mikro Companion** port. In Pad Mode, turn the encoder to shift the range by 16 notes. The screen shows the current range. Hold Group and use the arrows to select tracks.

Play and Stop control transport. Rec toggles **Session Record** in Session View and **Arrangement Record** in Arrangement View. With both views open, it follows the focused window. Arm tracks manually in Live or with another controller. Starting Session recording opens the recording clip in the lower Clip View once it exists, including after a count-in or quantized launch.

**Follow** toggles Live's playback follow/scroll setting. **Shift + Follow (Grid)** opens the grid setting described under Step sequencer.

With a Drum Rack, the pads follow its visible 4-by-4 grid. Changing pages on the Mikro scrolls the grid in Live; scrolling it in Live updates the Mikro. For other instruments, the initial range is notes 36-51 (C1-D#2), and each track keeps its range until the script is reloaded.

Filled Drum Rack pads light Dim in the selected track's closest hardware palette color. A pad counts as filled when it has a chain. Empty pads stay dark unless selected or struck. Other instruments light all 16 pads.

A strike on the Mikro selects that note. The selected pad stays at medium brightness and flashes Bright when played; other pads flash at medium brightness. Flashes last at least 50 ms. Clips and other MIDI inputs flash pads without changing the selection. Each track and Drum Rack keeps its selection for the script session. Changing banks keeps the selected pitch, even when it is outside the visible page. Selecting a Drum Rack pad in Live updates the Mikro selection.

Drum Rack pads containing Simpler also flash when the sampler starts or retriggers. This follows playback from clips and other controllers, with Live's transport running or stopped. Simpler can be nested inside an Instrument Rack within the pad.

Clip note starts also flash within the displayed range, including multisample pads whose playback position is unavailable. For live flashes from another controller, list its Windows MIDI input names in `MidiActivityInputs` in companion.ini, separated by semicolons. For example:

```ini
MidiActivityInputs=Launchkey MK4 37 MIDI;MIDIIN2 (Launchkey MK4 37 MIDI)
```

The names support `*` wildcards. An empty value disables additional input listeners. These listeners read strikes in parallel with Live and close when the companion pauses. They do not forward or generate notes.

Live's MIDI routing and monitoring settings determine which instruments receive the notes. Held notes keep their original pitch until released, even after a page change.

Play and Stop use Bright during playback and Dim when stopped. Rec uses Bright when recording is enabled in the focused view, and Dim otherwise.

Tap sets Live's tempo from repeated presses. Hold Shift and press Tap to toggle the metronome. Tap lights brightly while the metronome is enabled and dimly while it is disabled, including changes made in Live.

Restart starts playback and returns the Arrangement playhead to 1.1.1. Shift + Restart toggles Live's Arrangement Loop without changing its boundaries. The Restart button is Bright while Loop is enabled and Dim otherwise.


## Pad Mode and Keyboard

**Pad Mode** keeps the 16-note instrument/drum layout; the encoder selects banks of 16 notes. Press the encoder to return to the default bank (notes 36-51). The clipped banks at either end return to the same 16-note alignment when scrolling back. Its button is Bright while active; Keyboard is Dim.

**Keyboard** switches to a melodic layout. Pads ascend from the bottom left, across each row and then upward. With Live's Scale Mode enabled, only notes in the current scale appear: for C Major, the first row is C, D, E, F. With Scale Mode disabled, the layout is chromatic. Root notes use white, or blue when the track color is white.

Turn the encoder to change the note on pad 1 by semitones, including octave crossings (B1 to C2). The other pads rebuild from that note, and Live's root follows its pitch class. Root notes remain highlighted.

Use the arrows to choose **Root Note**, **Scale** or **Scale Mode**, then turn the encoder to change its value. On Scale Mode, turn right for On or left for Off, preserving the root and chosen scale. Shift + encoder press has no action in Keyboard. Press the encoder without Shift to return pad 1 to the default octave for the current root note.

Each track remembers its Keyboard starting octave. Switching to Pad Mode restores its separate drum bank. Changes to Live's root keep the current Keyboard octave and rebuild the pads from the new tonic. Held notes keep their original pitch until released. Switching between Pad Mode and Keyboard releases held notes and exits 16 Vel.


The scale layout uses Live 12's standard 12-tone scale intervals. It does not implement custom tuning layouts.

## Track selection (beta)

Hold **Group** and press an arrow to select the previous or next visible track. Return tracks and Main follow ordinary tracks. Navigation stops at the ends, and the arrow lights show whether another track is available. Track selection does not arm tracks. **Group + Shift + right arrow** inserts and selects a MIDI track after the selected track; from a return track or Main it adds one at the end. The right arrow lights Bright for this command even at the end of the track list. Release Group to restore the current mode's screen and controls. Mute and Solo still affect the selected track.

## Events: note selection

Open a MIDI clip on the selected track in Live's Clip editor, then press **Events**. The button becomes Bright and the controller screen shows only **NOTE SELECT**. Live stays in its current Session or Arrangement view. Events is unavailable while the Clip editor is hidden, an audio clip is shown, or the displayed clip belongs to another track.

Pressing Events from Step switches to note selection in the same clip, using the Pad Mode or Keyboard pitch layout from before Step. Events is Bright; the underlying layout button is Dim.

In Events, pads select notes without playing them. Press a pad to select every event at its pitch; other pads add their pitches, and pressing a chosen pad again removes it. This works with the current Pad Mode bank or Keyboard scale layout. Pads with clip events light Dim; chosen pitches use medium brightness. Playback flashes remain active.

Turn the encoder to select one event at a time across the chosen pitches, ordered by time. Hold Shift while turning to extend or shrink a contiguous range from the current event. Releasing Shift keeps that range; the next unshifted turn replaces it with a single event. Shift + pads keeps the printed editing commands, acting on the selection visible in Live.

In Events, **Erase** deletes only the notes selected in Live. It is Dim when a selection exists and off otherwise. The deletion is one undo step; an empty selection deletes nothing.

Press Events again to return to playing. The selection in Live stays in place. Changing tracks or closing the Clip editor exits Events. Opening another MIDI clip on the same track keeps Events active and resets its pitch groups and event cursor. Pad Mode and Keyboard retain their independent ranges; entering Events exits 16 Vel.

## Editing with Shift

Hold Shift to use the commands printed on the pads in Pad Mode or Keyboard. Available commands light Dim white and flash Bright white when pressed. These presses do not play notes or change the selected note. Notes held before Shift still release normally.

| Pad | Command |
| --- | --- |
| 1 / 2 | Undo / Redo in Live |
| 5 / 6 | Quantize / Quantize 50% |
| 7 / 8 | Nudge left / right |
| 13 / 14 | Semitone down / up |
| 15 / 16 | Octave down / up |

Editing uses the MIDI clip shown in Live's Clip editor, provided it belongs to the selected track. Commands affect selected notes; if no notes are selected, they affect all notes in that clip. Undo and Redo use Live's global history. Step Undo/Redo, Clear, Clear Auto, Copy and Paste are unassigned.

Quantize moves note starts to the current clip grid; Quantize 50% moves them halfway there. Straight and triplet grids are supported. Nudge moves notes by half that grid division, preserving relative timing, and stops at time zero. With the grid off, quantize and nudge are unavailable. Semitone and Octave transpose by exactly 1 and 12 semitones, including when Scale Mode is on. A transpose that would exceed MIDI's pitch range is unavailable.

Each edit is one undo step. Note durations, velocities and other note properties are preserved. Commands with no applicable change stay dark.

## Volume and tempo

**Volume** assigns the encoder to the selected track's fader. Turn for fine adjustment, or hold Shift to move in whole dB. Press the encoder to reset to 0 dB. The screen shows Live's dB value, including changes made with the mouse. Silence appears as -INF dB. Press Volume again to return to the previous encoder mode.

**Tempo** assigns the encoder to BPM in whole steps of 1 BPM, including with Shift. The screen shows the tempo; pressing the encoder leaves it unchanged. Press Tempo again to return. Volume and Tempo leave the pad layout intact.

## Browser

**Browse** opens Live's browser and focuses its result list. Turn the encoder to move one item, or hold Shift to jump six items per detent without selecting the intermediate items. Jumps stop at the beginning or end of the list. Press the encoder in Library to enter the selected category. In the result list, pressing expands or collapses an instrument or folder; on a preset, it loads the preset and returns to the previous controller mode. In the grouped Plug-Ins list, pressing opens a plug-in's preset branch. If it is empty, the plug-in loads on that same press after the preset check. The companion checks for presets for up to half a second, stopping as soon as they appear. **Shift + encoder press** moves to Library. Group + arrows select tracks.

Clicking outside the browser exits Browse, as does pressing Browse again. Preview follows Live's browser preview setting. Keyboard commands are sent only to Live's main window.

Press **Favorites** to add or remove the selected browser item from Live's first, red Collection (Favorites in the default layout). Its button is Bright for presets found in that Collection's saved metadata and Dim otherwise. The light may take a second to update. Membership is matched by preset filename; identically named presets in different folders can share the same indication.

## Pattern: launch clips

Press **Pattern** to enter a 4-by-4 Session grid. Columns are tracks; rows are scenes, with the top row matching the first scene in the frame shown in Live. Pads use the clips' colors. Playing clips are Bright, queued launches blink, and recording clips use red.

Press an empty slot to select it and its track without creating a clip or starting recording. The selected empty slot lights white; other empty slots stay dark. Hold **Select** and press any pad to select a filled or empty slot without launching it. A selected filled clip opens in Live's Clip editor and uses medium pad brightness when stopped. Track arming stays manual.

While Select is held, available empty slots light Dim white, occupied slots keep their clip colors, and the selected slot is Bright. Missing track columns and scene rows stay dark and inactive. Playback brightness and blinking give way to selection brightness until Select is released. The encoder scrolls scenes; hold Shift to scroll track columns. Pattern and Select are Bright; Group remains Dim for track navigation. Other command buttons are dark and blocked. The screen shows the chosen Scenes or Tracks parameter and its range. Select takes priority over Shift, so holding both does not expose the Stop layer. Releasing Select restores the launch grid without launching or stopping a clip.

After selecting an empty MIDI slot, press **Step**; the first step creates and launches the clip. To record a performance, switch to Pad Mode or Keyboard, arm the track and use Rec with Live's normal Session recording settings. Selecting the slot also selects its scene; Session Record can record other armed tracks according to Live's settings.

Turn the encoder to scroll by one scene, or hold **Shift** and turn to scroll by four tracks. Scrolling the frame does not change the selected Live track. Return tracks and Main are excluded from the frame. A folded group follows Live's visible track list; group slots without their own clip stay inactive.

Pattern stays active after releasing its button. Press it again to restore the previous mode, or press Pad Mode, Keyboard or Step to enter that mode directly. Launches follow Live's clip launch mode and quantization settings. The pads do not play instrument notes or invoke Shift editing commands in Pattern. The OLED shows the track and scene range.

Hold **Shift** to enter the Stop layer. Only the bottom four pads are active, one per track column; the upper twelve pads are dark and ignore presses. Bright red means a Session clip is playing on that track; dim red means it is stopped. The indication includes clips outside the visible grid. Press a red pad to stop that track's clips with Live's normal stop quantization. The OLED shows TRACKS and the visible track range. Release Shift to restore the full 4-by-4 clip grid. Transport buttons retain their usual actions.

## Scene: launch rows of clips

Press **Scene** to enter a separate scene-launch mode. Only the four pads in the rightmost column are active: from top to bottom they launch the four scenes in the shared Session frame. Other pads are dark and inactive. The active pads use the scenes' colors; a queued scene blinks, and a scene with playing clips lights Bright. The OLED shows SCENE and the current range.

Turn the encoder to scroll by one scene, stopping at the ends. Pattern and Scene share their track and scene offsets, so switching between them keeps the same frame. Arrows are inactive in Scene. Launching a scene uses Live's normal scene action across all its tracks, including tracks outside the frame, and follows Live's launch quantization and scene settings.

Press Scene again to return to Pattern if you entered from Pattern, or to the previous playing/editing mode otherwise. Pattern, Pad Mode, Keyboard and Step enter their modes directly. Transport buttons retain their usual actions.

## Step sequencer

Play a note in Pad Mode or Keyboard to choose the sound, then press **Step** with a MIDI clip open or an empty Session clip slot selected on that MIDI track. Selecting only the MIDI track also works in Session when its cell in the selected scene is empty; Step uses that cell if the highlighted slot still belongs to another track. Entering Step on an empty slot creates nothing yet. The first step press creates a one-bar MIDI clip, adds the note and launches the clip immediately. The bar length follows the song time signature; the track's Arm state stays unchanged. The screen shows STEP PAGE and its page counter. Each pad toggles one step at the clip's current grid resolution, starting with pad 1. Empty slots use the current Step grid setting, initially 1/16. Occupied steps light at medium brightness; the current playback step is white and Bright. Turn the encoder to move through bounded 16-step pages. Group + arrows select tracks; changing tracks exits Step. Press Step again, Pad Mode or Keyboard to leave the sequencer.

Hold **Shift + Follow (Grid)** and turn the encoder to choose 1/4, 1/8, 1/16 or 1/32, with a triplet option after each division. The OLED highlights the chosen resolution. Release either button to return to the previous screen and encoder function; the chosen grid stays set. This changes the MIDI clip's editor grid and Step spacing; existing notes keep their positions and lengths. The page count, playback cursor and Smart Strip overview follow the new spacing.

Notes recorded by hand belong to the nearest step, including hits played slightly early. Entering Step leaves their timing and velocity intact. Pressing an occupied step removes its existing notes at the selected pitch instead of adding a duplicate. If several hits fall on that step, they are removed together in one undo action. In a loop, an early hit just before the loop end can belong to the first step.

When Step is entered from Pattern or Scene, pressing Step again returns to that grid at the same position. If the editing target becomes unavailable, the grid also returns. Choosing Pad Mode, Keyboard, Events, Pattern or Scene explicitly replaces this return destination.

Hold **Select** in Step to audition and select a sound using the pad input mode from before entering Step. From **Pad Mode**, pads use chromatic 16-note banks and the encoder scrolls those banks, with or without a Drum Rack. From **Keyboard**, pads follow the chosen root, scale and Scale Mode; the encoder changes Root Note, even with a Drum Rack loaded. The screen shows PAD BANK or ROOT NOTE above the encoder value. Press the encoder while holding Select to restore the default kit bank (starting at C1, MIDI note 36) or the default Keyboard octave for the current root. The other mode's range, chosen scale and Step page are preserved. Select and the corresponding Pad Mode or Keyboard button are Bright. Played notes and clip activity keep their normal pad flashes. Ordinary arrows and other commands are inactive during this temporary selection; Group still provides track navigation. Release Select to return to Step on the same page with the newly selected pitch. Held audition notes stop on Select release; release their pads before pressing them as steps.

Shift retains the printed pad editing commands in Step, including Undo and Redo.

Playing another controller does not select the sequencer pitch.

**Duplicate** doubles the clip's loop, including its notes. It is available in Step for looped clips. Each pad edit or duplication is one undo step. Step exits when the selected clip or track changes. When editing an existing clip, closing its editor also exits Step.

The Smart Strip's 25 LEDs represent the entire clip loop, or the marker range for an unlooped clip. The background uses the clip's palette color, the editing page is white, and a Bright point shows playback position. Longer clips compress proportionally into the same strip. Strip color and brightness still need hardware confirmation.

## Note Repeat

In **Pad Mode**, hold **Note Repeat** and play pads. Held notes repeat together; releasing a pad stops its repeats. Releasing Note Repeat stops the repeated notes and returns the encoder and screen to their previous mode. Play a pad again for a normal sustained note.

While holding Note Repeat, turn the encoder to choose the rate, from 1/1 to 1/128, including triplets marked **T**. The default is **1/16**. Also hold **Swing** and turn the encoder to adjust swing from **50%** (straight) to **75%**. Swing delays alternate hits at the chosen straight rate. Triplet rates stay evenly spaced. Use the arrows to choose Rate or Swing; the encoder changes the displayed value. Swing + encoder remains a shortcut to swing. Settings remain until the companion exits.

Repeat follows Live's tempo and aligns to its transport grid during playback. With transport stopped, the first held pad starts a local cycle at that tempo. Notes use the strike velocity, or Fixed Vel / 16 Vel when enabled. MIDI recording captures the generated notes with Live's usual Arm and recording settings. Groove Pool processing remains a separate Live setting.

While holding a pad with Note Repeat, press harder or ease off to change the velocity of subsequent repeats. Each held pad responds independently. Live records these changes as note velocities. Fixed Vel and 16 Vel keep their assigned velocities regardless of pressure.

The repeat timer runs separately from the interface and panel output. It requests a 1 ms Windows timer period only while notes are repeating. Pause, disconnection, track changes and a missing Live clock stop repeats. Step, Events and Browse leave Note Repeat unavailable.

## Arpeggiator in Keyboard

In **Keyboard**, press **Note Repeat** to enable Arp and open its settings. Release the button; Arp stays enabled and plays the held pads as a sequence. You can also enable it on an already held chord. Releasing the pads stops the notes unless Latch is enabled. Press Note Repeat again while its settings are shown to turn Arp off. Held pads must then be released before they can play ordinary notes again.

Use the arrows to select a parameter, then turn the encoder to change its value. Selection stops at the first and last parameters. The display shows the parameter above its inverse value.

**Shift + Note Repeat** toggles **Latch** while Arp is enabled, without changing the current screen. Latch keeps the chord playing after all pads are released. The first new pad press after all pads are released replaces that chord; additional pads join while at least one remains physically held. Each note retains its initial strike velocity, including after release. Turning Latch off releases remembered notes immediately; physically held notes continue. While Shift is held, Note Repeat is Dim for Latch Off and Bright for On. Without Shift it shows the usual Arp state. The shortcut does nothing when Arp is off. Latch and its chord clear when Arp stops, the track changes, or the companion pauses or loses its connection.

| Parameter | Values |
| --- | --- |
| Rate | 1/1 to 1/128, including triplets; initially 1/16 |
| Type | Up, Down, Up-Down, Played, Chord; initially Up |
| Octaves | 1-8; initially 1 |
| Gate | 1-100%; initially 50% |
| Swing | 50-75%; initially 50% |
| Rhythm (beta) | 01-64; initially 01 (all steps play) |

On the last Arp page, **Rhythm (beta)**, turn the encoder to choose one of 64 fixed eight-step patterns. The original patterns keep numbers 01-16; 17-24 add sparse pairs, 25-40 use three hits, 41-56 use four hits, and 57-64 add denser figures. The screen shows its number above eight cells: filled means a note, hollow means a rest, read left to right. Pattern 01 plays every step; 09 is Tresillo and 10 is Cinquillo. Each cell lasts one Rate division, so changing Rate changes the duration of the eight-step cycle. Rests keep the clock running but do not consume a note from the chord order. Changing the pattern preserves the current grid position. The screen diagram changes only when you select a pattern; pads keep their note flashes. Rhythm works with Latch and all Arp types, and remains selected until the companion exits.

Up-Down avoids repeating the endpoint notes. Played follows the order in which pads were pressed. Chord repeats all held notes together. Octaves extends upward, omitting pitches above MIDI note 127. Each source note and its octave copies use the velocity of the initial strike. Pressure changes do not alter Arp velocity, with or without Latch. Fixed Vel and 16 Vel supply their assigned strike values. Pads flash for the source notes being generated.

Press **Keyboard** to return to root and scale control while Arp continues. Note Repeat stays Bright to show that it is enabled. Press Note Repeat to reopen its settings; pressing it again turns Arp off. Encoder clicks retain their Keyboard assignments: ordinary click resets the starting octave; Shift + click has no action. Use the third Keyboard page to change Scale Mode. Group + arrows select tracks.

Volume, Tempo and held Grid temporarily take over the encoder. Closing them restores the previous Arp page or Keyboard view. Switching to Pad Mode, Step, Pattern, Scene, Events or Browse stops Arp, as do track changes, pause, disconnection and clock loss. Transport buttons keep their existing actions. With transport stopped, held pads arpeggiate using the local clock, as with Note Repeat.

Live records the generated MIDI notes using the track's normal Arm and monitoring settings. Arp settings are separate from drum Note Repeat and remain for the companion session. Dotted durations, accents, ratchets, ties and Gate above 100% are not implemented.

## Pitch and Mod

In Pad Mode and Keyboard, **Pitch** assigns the Smart Strip to MIDI pitch bend. The center LED marks standard tuning; releasing the strip returns the bend to center. **Mod** assigns the strip to CC1 and keeps the value after release. The LEDs show the current position. Press the active button again to turn that strip mode off.

Step uses the strip for its pattern display. Browse and Events suspend Pitch/Mod. Pausing the companion or changing tracks clears pitch bend and modulation. The instrument determines its bend range and response to CC1.

## Velocity and mixing

**Fixed Vel** toggles constant velocity for the normal pad layout. Set `FixedVelocity=127` in companion.ini to choose a value from 1 to 127. The default is 127.

**Shift + Fixed Vel** toggles **16 Vel**. All 16 pads play the selected note, with velocity increasing from 1 on pad 1 to 127 on pad 16. The screen shows the current note bank or Keyboard parameter. Each row uses a higher brightness level than the row below it, using all four available levels from pad 1 to pad 16. Struck pads use the brightest level. If no note is selected, the button briefly goes dark and the mode stays off. Fixed Vel and 16 Vel replace each other; disabling either returns to normal playing. Changing the selected track or Drum Rack exits 16 Vel.

The Fixed Vel button is Bright while either mode is enabled and Dim otherwise. Mode changes release held notes. When several pads play the same note in 16 Vel, each strike retriggers it; the final pad release ends the note.

**Mute** toggles the selected regular or group track. **Solo** also works on Return tracks. Mute and playable note pads are disabled on Return and Main tracks; Volume, track navigation and transport remain available. The buttons are Dim when off and Bright when on, and follow changes made in Live. Solo respects Exclusive Solo. Both buttons are off on Main.

## Current boundaries

- Uses Windows MIDI Services loopback support. Port creation was verified on the development PC; other Windows builds need compatibility testing.
- Drum Rack synchronization also finds kits inside Instrument Racks. When several kits are present, it prefers the selected device and chain, then the first available kit. Drum Racks inside another Drum Rack use the outer rack's grid.
- Pattern selects empty Session slots without creating clips. Step creates a MIDI clip on the first step edit; recording from pads uses Live's normal Arm and Session Record settings.
- Direct sampler activity requires a Simpler with an available playback position; multisample Simpler devices do not provide this. Clip lighting uses stored note starts and cannot reproduce the result of note chance or MIDI effects. Additional MIDI input flashes show controller strikes regardless of Live's track routing.
- Live supplies pad ranges, colors and transport state over local UDP port 39473. Notes travel through the MIDI port. A missing heartbeat releases notes and suspends the integration.
- Switching to MIDI mode pauses the native integration. Return to native mode, then choose **Resume** in the companion tray menu. USB reconnection, restarting Live and Reload config do not clear this pause.
- The companion reuses its private MIDI pair after an interrupted exit and removes it on ordinary exit.

## Verification

See the [changelog](../CHANGELOG.md) for released features and the [development plan](../ROADMAP.md) for proposed additions.

`Build-Companion.ps1` runs the launcher tests and replays a captured native HID session. `Test-Midi.ps1` creates a temporary pair, verifies exact delivery of notes, velocities, transport and held-note releases, then removes it. Run it with the companion closed after building the native bridge.

The Live script tests run with `python -m unittest discover -s tests -p 'test_ableton_*.py'`.

Windows MIDI API source and SDK: [Microsoft MIDI](https://github.com/microsoft/MIDI/releases/tag/inbox-preview-11). Live script installation: [Ableton](https://help.ableton.com/hc/en-us/articles/209072009-Installing-third-party-remote-scripts).

Browser navigation: [Live keyboard shortcuts](https://www.ableton.com/en/live-manual/12/live-keyboard-shortcuts/). Clip editing: [Live Clip API](https://docs.cycling74.com/apiref/lom/clip/).
