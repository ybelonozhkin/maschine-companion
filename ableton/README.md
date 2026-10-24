# Ableton Live integration

Use the Mikro MK3 in native mode to play instruments, edit MIDI clips and browse sounds. Tested with Live 12.4.6 Standard on Windows 11 x64.

## Setup

Follow [Getting started](../GETTING-STARTED.md#ableton-live-installation) for requirements, installation and MIDI port settings. To compile the app yourself, use [Build and test](../CONTRIBUTING.md#build-and-test).

Start the companion before Live. Use **MikroCompanion** as the control surface, **Mikro Companion** as its Input and **None** as Output. Arm tracks manually for recording.

## Pause before using Maschine

Choose **Pause** in the tray before loading Maschine as a plug-in or giving the controller to another application. Close/remove Maschine, return to native mode and choose **Resume**. Use the indefinite pause for this; **Pause for 1 hour** resumes automatically.

## Display and lighting

The OLED shows the current parameter and value. An inverse value is the encoder target; lit arrows indicate another parameter page. Hold **Group** to see the selected track's number and name.

Check **Dim** in the tray for minimum OLED contrast, or uncheck it for normal contrast. The setting is saved.

Available buttons are dim and active functions are bright. Drum Rack pads show occupied cells in the track color. The selected sound stays brighter; played notes flash. Other instruments light all 16 pads.

## Playing and transport

Pads send velocity-sensitive MIDI notes to the track receiving **Mikro Companion** input.

| Control | Action |
| --- | --- |
| Play / Stop | Start / stop Live's transport. |
| Rec | Toggle Session Record or Arrangement Record, following the focused view. |
| Tap | Tap tempo. |
| Shift + Tap | Toggle the metronome. |
| Restart | Start playback from Arrangement position 1.1.1. |
| Shift + Restart | Toggle Arrangement Loop. |
| Follow | Toggle playback follow/scroll. |
| Shift + Scene | Switch between Session and Arrangement views. |

In Session, select a scene or clip before recording; the recording clip opens in the editor. In Arrangement, place the playhead and use Rec and Play. Rec alone does not start Arrangement playback. Track arming remains manual.

To show live pad flashes from another controller, list its MIDI input names in `companion.ini`, separated by semicolons:

```ini
MidiActivityInputs=Launchkey MK4 37 MIDI;MIDIIN2 (Launchkey MK4 37 MIDI)
```

Names support `*` wildcards. Leave the value empty to disable this. These lights show incoming strikes regardless of Live's track routing.

## Arrangement navigation

Press **Shift + Variation (Navigate)** to enter or leave navigation. The arrows select two pages:

| Page | Encoder | Shift + encoder |
| --- | --- | --- |
| Position | Move one bar | Move four bars |
| Markers | Previous / next Arrangement locator | Same |

The screen shows the position or locator name. Moving while stopped sets the next Play position; moving during playback keeps transport running. Pads retain their current function.

Navigation works in both Session and Arrangement views and remembers the last page. **Shift + Scene** switches the visible view while keeping the current controller mode. The arrows stop at the first and last navigation page.

## Pad Mode and Keyboard

**Pad Mode** provides banks of 16 notes. Turn the encoder to change banks; press it to return to notes 36-51 (C1-D#2). With a Drum Rack, the controller and Live's visible pad grid follow each other.

**Keyboard** lays out ascending notes from the bottom-left pad. With Scale Mode on, pads use only the selected scale; with it off, they are chromatic. Root notes are highlighted.

Use the arrows to choose a parameter, then turn the encoder:

| Page | Encoder action |
| --- | --- |
| Root Note | Change the starting note by semitones and update Live's root. |
| Scale | Choose Live's scale. |
| Scale Mode | Turn right for On, left for Off. |

Press the encoder to reset the starting octave for the current root. Each track remembers separate Pad Mode and Keyboard ranges. The layout uses Live's standard 12-tone scales.

## Track selection (beta)

Hold **Group** and use the arrows to select tracks, including Returns and Main. This does not arm the track.

**Group + Shift + right arrow** creates and selects a MIDI track after the current track, or at the end when a Return or Main is selected.

## Events: note selection

Open a MIDI clip on the selected track in Live's Clip editor, then press **Events**. The screen shows **NOTE SELECT**. Events works in both Session and Arrangement.

Pads select notes without playing them. Press a pad to select all notes at its pitch; press other pads to add pitches, or press a selected pad again to remove it. The layout follows Pad Mode or Keyboard.

Turn the encoder to select individual events across those pitches. Hold Shift while turning to extend or shrink a range. The next turn without Shift returns to a single event.

**Erase** deletes the selected notes. Shift + pads applies the printed editing commands. Press Events again to return to playing. Changing tracks or closing the Clip editor exits Events.

## Editing with Shift

Hold Shift in Pad Mode, Keyboard or Step to use the commands printed on the pads. Available commands light dim white and flash when pressed.

| Pad | Command |
| --- | --- |
| 1 / 2 | Undo / Redo in Live |
| 5 / 6 | Quantize / Quantize 50% |
| 7 / 8 | Nudge left / right |
| 13 / 14 | Semitone down / up |
| 15 / 16 | Octave down / up |

Edits use the MIDI clip open on the selected track. They affect selected notes, or **all notes in the clip if nothing is selected**. Undo and Redo use Live's global history.

Quantize moves notes to the straight clip grid; Quantize 50% moves them halfway to it. Nudge moves by half a grid division. Both are unavailable with the grid off. Transpose moves by 1 or 12 semitones, including with Scale Mode on. Each edit is one undo step.

Step Undo/Redo, Clear, Clear Auto, Copy and Paste are unassigned.

## Volume and tempo

| Mode | Encoder | Shift + encoder | Encoder press |
| --- | --- | --- | --- |
| Volume | 0.5 dB steps | 0.1 dB steps | Reset to 0 dB |
| Tempo | Whole BPM | Whole BPM | No action |

The screen shows Live's current value. Press the mode button again to return to the previous encoder function.

## Browser

Press **Browse** to navigate Live's browser:

- Turn the encoder to move one item; hold Shift to jump six.
- Press the encoder to enter a category, expand/collapse a folder or load a preset. A plug-in with no presets loads directly.
- **Shift + encoder press** returns to Library.
- Loading a preset, clicking outside the browser or pressing Browse again leaves this mode.

**Favorites** adds or removes the selected item from Live's first, red Collection. The button is bright for items already in that Collection. Its indication uses preset filenames, so identically named presets can share the same light state.

## Pattern: launch clips

Press **Pattern** for a 4-by-4 Session grid: columns are tracks and rows are scenes. Pads use clip colors; playing clips are bright, queued launches blink and recording is red.

Press a filled pad to launch its clip. An empty pad selects that slot without recording or creating a clip. Hold **Select** to select any slot without launching it and open filled clips in the editor. While Select is held, available empty slots are dim white and the selected slot is bright; missing tracks or scenes stay dark.

Turn the encoder to scroll scenes; **Shift + encoder** scrolls four tracks at a time. This also works while holding Select.

Hold **Shift** for track Stop: only the bottom four pads are active, one per column. Bright red means playing; dim red means stopped. Press a pad to stop that track's clips, including those outside the visible grid. Release Shift to return to clip launching. Select takes priority if both are held.

Launch and stop actions follow Live's quantization. Press Pattern again to return to the previous mode. Return tracks and Main are outside this grid.

## Scene: launch rows of clips

Press **Scene** to launch four scenes from the rightmost pad column, top to bottom. Turn the encoder to scroll scenes. Pattern and Scene share the same frame position.

A scene launches across all its tracks, including those outside the frame. Scene colors, bright playback indication and blinking queued launches follow Live. Press Scene again to return to the previous mode.

## Step sequencer

Choose a sound in Pad Mode or Keyboard, then press **Step** with a MIDI clip open or an empty Session slot selected on that MIDI track. In Session, selecting only the track also works when its slot in the selected scene is empty.

Each pad toggles a step at the current clip grid resolution. On an empty slot, the first step creates and immediately launches a one-bar MIDI clip. Turn the encoder to move through 16-step pages. Occupied steps are lit; the playing step is bright white.

Hold **Shift + Follow (Grid)** and turn the encoder to choose 1/4, 1/8, 1/16 or 1/32, including triplets. Release either button to leave Grid. Changing the grid leaves existing notes in place.

Hand-played notes are recognized at their nearest step without changing their timing or velocity. Pressing an occupied step removes its notes at the selected pitch, including multiple hits assigned to that step.

Hold **Select** to audition and choose another sound:

- From Pad Mode, the encoder changes 16-note banks; press it to reset to the default bank.
- From Keyboard, pads follow its scale and the encoder changes Root Note; press it to reset the octave.

Release Select to return to steps. Release audition pads before using them as step buttons. Playing another controller does not change the selected sound. Shift keeps the printed editing commands available.

**Duplicate** doubles a looped clip. The Smart Strip shows the clip overview: clip color for the background, white for the editing page and a bright playback marker. Strip color and brightness still need hardware confirmation.

Press Step again to return to the mode you entered from, including Pattern or Scene. Changing the selected clip or track exits Step; closing an existing clip's editor also exits it.

## Plug-in parameters

Press **Plug-in** to control the selected device on the current track. Press it again to return. Pads keep their current playing mode.

| Control | Action |
| --- | --- |
| **Left / Right** | Select the previous or next parameter. |
| **Shift + Left / Right** | Select the previous or next device in the chain. |
| **Encoder** | Change a continuous parameter by 1/127 of its range; a discrete parameter by one choice. |
| **Shift + encoder** | Adjust continuous parameters ten times more finely; discrete parameters still move by one choice. |

The display shows the parameter name and its value in Live's units. Device names appear briefly when switching devices. Navigation stops at the ends; unassigned directions stay dark. Selecting another device with the mouse also changes the controller's target.

For third-party plug-ins, expose the controls you want through Live's **Configure** panel. Devices inside a Rack chain can first be selected in Live; Shift + arrows then navigates that chain.

## Note Repeat

In **Pad Mode**, hold **Note Repeat** and play pads to repeat their notes. Use the arrows to choose Rate or Swing, then turn the encoder. Holding **Swing** also gives direct access to swing.

Rate ranges from 1/1 to 1/128, including triplets marked **T**; the default is 1/16. Swing ranges from **50% (straight)** to 75% and does not affect triplets.

Repeat follows Live's tempo and playback grid. With transport stopped, it runs at the same tempo using a local clock. Press harder or ease off to change subsequent repeat velocities. Fixed Vel and 16 Vel keep their assigned values. Live records the generated notes with the track's normal Arm settings.

**Shift + Note Repeat** pins Note Repeat so you can release the button. Press **Note Repeat** without Shift to turn it off. Pads still need to be held; releasing them stops their notes. While Shift is held, a bright Note Repeat button means the mode is pinned.

Without pinning, releasing Note Repeat turns it off. Track changes, pause or disconnection also stop it and clear pinning. Note Repeat is unavailable in Step, Events and Browse.

## Arpeggiator in Keyboard

In **Keyboard**, press **Note Repeat** to enable Arp or reopen its settings. Press it again while those settings are shown to turn Arp off. Arp plays held pads as a sequence. Use the arrows and encoder to adjust:

| Parameter | Values |
| --- | --- |
| Rate | 1/1 to 1/128, including triplets; default 1/16 |
| Type | Up, Down, Up-Down, Played, Chord; default Up |
| Octaves | 1-8; default 1 |
| Gate | 1-100%; default 50% |
| Swing | 50-75%; default 50% |
| Rhythm (beta) | 64 eight-step patterns; default 01 (all steps play) |

**Shift + Note Repeat** toggles **Latch** while Arp is enabled. It keeps the chord playing after you release the pads; play a new chord to replace it. While Shift is held, a bright Note Repeat button means Latch is on. Each note keeps its initial strike velocity; pressure does not change Arp velocity.

On **Rhythm (beta)**, the screen shows eight cells: filled for a hit, hollow for a rest. Each cell lasts one Rate division. Pattern 01 plays every step; 09 is Tresillo and 10 is Cinquillo.

Played follows pad order; Chord repeats all notes together. Up-Down avoids repeating its endpoints. Octaves extends the pattern upward.

Press **Keyboard** to adjust root and scale while Arp continues. Volume, Tempo and Grid also remain available. Leaving Keyboard for Pad Mode, Step, Pattern, Scene, Events or Browse stops Arp. Track changes, pause and disconnection clear Latch.

Live records the generated notes with its normal Arm and monitoring settings. Arp and drum Note Repeat keep separate settings until the companion exits.

## Pitch and Mod

In Pad Mode and Keyboard, **Pitch** assigns the Smart Strip to pitch bend and returns to center on release. **Mod** sends CC1 and keeps its value after release. Press the active button again to turn that strip mode off.

The instrument determines bend range and modulation response. Track changes or pausing reset both. Step uses the strip for its overview; Browse and Events suspend Pitch/Mod.

## Velocity and mixing

**Fixed Vel** toggles constant pad velocity. Set its value with `FixedVelocity` (1-127, default 127) in `companion.ini`.

**Shift + Fixed Vel** toggles **16 Vel**: all pads play the selected sound, from velocity 1 on pad 1 to 127 on pad 16, with increasing brightness. Select a sound first. Fixed Vel and 16 Vel replace each other; changing tracks or Drum Racks exits 16 Vel.

**Mute** and **Solo** affect the selected track. Solo respects Live's Exclusive Solo setting. On Return tracks, Solo and Volume work but Mute and note pads are unavailable. On Main, use Volume; Mute, Solo and note pads are unavailable.

## Limitations and recovery

- Arp Rhythm and track navigation are beta. Other Windows and Live setups need compatibility testing.
- With several Drum Racks, select the intended device or chain in Live.
- Pad flashes use Simpler activity, clip notes or configured MIDI inputs. They may not reflect note chance or MIDI effects; multisample Simpler pads rely on clip/input feedback.
- After switching to MIDI mode, return to native mode and choose **Resume** in the tray.
- If the Live connection is lost, generated notes stop and the integration pauses.

For tests, see [Build and test](../CONTRIBUTING.md#build-and-test). For future features, see the [roadmap](../ROADMAP.md).
