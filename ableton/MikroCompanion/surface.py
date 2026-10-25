import socket
import time
import base64

from _Framework.ControlSurface import ControlSurface
from _Framework.ButtonElement import ButtonElement
from _Framework.InputControlElement import MIDI_CC_TYPE

from .model import pad_color, next_pad_bank, numbered_track_name
from .activity import SamplerActivity
from .clip_activity import ClipActivity
from .keyboard import Keyboard
from .editing import Editor
from .selection import NoteSelection
from .devices import DeviceControl
from .mixer import Mixer
from .navigation import Navigation
from .browser import Browser
from .sequencer import Sequencer
from .pattern import PatternGrid
from .ownership import inspect_song


class MikroCompanion(ControlSurface):
    """Live state and transport. Pad notes enter tracks through the MIDI port."""

    def __init__(self, c_instance):
        super(MikroCompanion, self).__init__(c_instance)
        self._socket = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._socket.setblocking(False)
        self._listeners = []
        self._layout_dirty = True
        self._state_dirty = True
        self._last_sent = 0
        self._ownership_at = 0
        self._ownership_track = None
        self._ownership_state = (False, 0, True)
        self._retry_at = 0
        self._last_error = None
        self._last_layout = None
        self._track = None
        self._rack = None
        self._mixer = Mixer(self.song())
        self._navigation = Navigation(self.song())
        self._last_navigation_packet = None
        self._last_navigation_error = None
        self._devices = DeviceControl(self.song(), lambda: self.application().view.show_view("Detail/DeviceChain"))
        self._device_previous = (False, False)
        self._last_device_packet = None
        self._last_device_error = None
        self._browser = Browser(self.application(), self._browser_key)
        self._browser_previous = (False, False)
        self._sequencer = Sequencer(self.song(), self._clip_editor_visible, self._session_view_focused)
        self._pattern = PatternGrid(self.song(), self._c_instance.set_session_highlight,
                                    lambda: self.application().view.show_view("Detail/Clip"))
        self._pattern_previous = None
        self._scene_previous_pattern = False
        self._step_return_grid = None
        self._last_pattern_packet = None
        self._last_step_packet = None
        self._last_grid_packet = None
        self._record_focus = None
        self._last_browser_packet = None
        self._last_mixer_packet = None
        self._selection = NoteSelection(self.song(), self._clip_editor_visible)
        self._last_selection_state = None
        self._last_selection_error = None
        self._editor = Editor(self.song(), self.log_message)
        self._last_edit_mask = None
        self._last_edit_error = None
        self._last_history = None
        self._keyboard = Keyboard()
        self._fallback_bases = {}
        self._selected_notes = {}
        self._last_target = None
        self._feedback_track = None
        self._feedback_epoch = int(time.monotonic() * 1000)
        self._activity_seen = False
        self._clip_activity = ClipActivity(self._pad_activity, self.log_message)
        with self.component_guard():
            self._play_button = ButtonElement(True, MIDI_CC_TYPE, 0, 20)
            self._stop_button = ButtonElement(True, MIDI_CC_TYPE, 0, 21)
            self._record_button = ButtonElement(True, MIDI_CC_TYPE, 0, 22)
            self._play_button.add_value_listener(self._play)
            self._stop_button.add_value_listener(self._stop)
            self._record_button.add_value_listener(self._record)
            self._bank_down_button = ButtonElement(True, MIDI_CC_TYPE, 0, 23)
            self._bank_up_button = ButtonElement(True, MIDI_CC_TYPE, 0, 24)
            self._bank_down_button.add_value_listener(self._bank_down)
            self._bank_up_button.add_value_listener(self._bank_up)
            self._tap_button = ButtonElement(True, MIDI_CC_TYPE, 0, 25)
            self._metro_button = ButtonElement(True, MIDI_CC_TYPE, 0, 26)
            self._tap_button.add_value_listener(self._tap)
            self._metro_button.add_value_listener(self._metronome)
            self._follow_button = ButtonElement(True, MIDI_CC_TYPE, 0, 27)
            self._follow_button.add_value_listener(self._follow)
            self._select_button = ButtonElement(True, MIDI_CC_TYPE, 0, 28)
            self._mute_button = ButtonElement(True, MIDI_CC_TYPE, 0, 29)
            self._solo_button = ButtonElement(True, MIDI_CC_TYPE, 0, 30)
            self._select_button.add_value_listener(self._select_note)
            self._mute_button.add_value_listener(self._mute)
            self._solo_button.add_value_listener(self._solo)
            self._pad_mode_button = ButtonElement(True, MIDI_CC_TYPE, 0, 31)
            self._keyboard_button = ButtonElement(True, MIDI_CC_TYPE, 0, 32)
            self._scale_encoder = ButtonElement(False, MIDI_CC_TYPE, 0, 33)
            self._scale_toggle_button = ButtonElement(True, MIDI_CC_TYPE, 0, 34)
            self._pad_mode_button.add_value_listener(self._pad_mode)
            self._keyboard_button.add_value_listener(self._keyboard_mode)
            self._scale_encoder.add_value_listener(self._scale_turn)
            self._scale_toggle_button.add_value_listener(self._scale_toggle)
            self._reset_range_button = ButtonElement(True, MIDI_CC_TYPE, 0, 36)
            self._reset_range_button.add_value_listener(self._reset_range)
            self._edit_button = ButtonElement(False, MIDI_CC_TYPE, 0, 37)
            self._shift_button = ButtonElement(True, MIDI_CC_TYPE, 0, 38)
            self._edit_button.add_value_listener(self._edit)
            self._shift_button.add_value_listener(self._editor.modifier)
            self._events_button = ButtonElement(True, MIDI_CC_TYPE, 0, 39)
            self._event_pad = ButtonElement(False, MIDI_CC_TYPE, 0, 40)
            self._event_encoder = ButtonElement(False, MIDI_CC_TYPE, 0, 41)
            self._event_range_encoder = ButtonElement(False, MIDI_CC_TYPE, 0, 42)
            self._events_button.add_value_listener(lambda value: self._events_action("toggle") if value else None)
            self._event_pad.add_value_listener(lambda pitch: self._events_action("pad", pitch))
            self._event_encoder.add_value_listener(lambda value: self._events_action("turn", value if value < 64 else value - 128))
            self._event_range_encoder.add_value_listener(lambda value: self._events_action("turn", value if value < 64 else value - 128, True))
            self._restart_button = ButtonElement(True, MIDI_CC_TYPE, 0, 45)
            self._loop_button = ButtonElement(True, MIDI_CC_TYPE, 0, 46)
            self._erase_button = ButtonElement(True, MIDI_CC_TYPE, 0, 47)
            self._restart_button.add_value_listener(self._restart)
            self._loop_button.add_value_listener(self._loop)
            self._erase_button.add_value_listener(lambda value: self._events_action("erase") if value else None)
            self._volume_button = ButtonElement(True, MIDI_CC_TYPE, 0, 48)
            self._volume_encoder = ButtonElement(False, MIDI_CC_TYPE, 0, 49)
            self._track_left_button = ButtonElement(True, MIDI_CC_TYPE, 0, 50)
            self._track_right_button = ButtonElement(True, MIDI_CC_TYPE, 0, 51)
            self._volume_button.add_value_listener(lambda value: self._mixer_action("toggle") if value else None)
            self._volume_encoder.add_value_listener(lambda value: self._mixer_action("turn", value if value < 64 else value - 128))
            self._track_left_button.add_value_listener(lambda value: self._mixer_action("navigate", -1) if value else None)
            self._track_right_button.add_value_listener(lambda value: self._mixer_action("navigate", 1) if value else None)




        with self.component_guard():
            for cc, action in ((52, lambda v: self._mixer_action("reset") if v else None),
                               (53, lambda v: self._mixer_action("fine", v if v < 64 else v - 128)),
                               (54, lambda v: self._mixer_action("toggle_tempo") if v else None),
                               (55, lambda v: self._mixer_action("tempo_turn", v if v < 64 else v - 128)),
                               (56, lambda v: self._mixer_action("tempo_turn", v if v < 64 else v - 128, True)),
                               (57, lambda v: self._browser_action("toggle") if v else None),
                               (58, lambda v: self._browser_action("turn", v if v < 64 else v - 128)),
                               (59, lambda v: self._browser_action("press", 13) if v else None),
                               (60, lambda v: self._browser_action("press", 37) if v else None),
                               (61, lambda v: self._step_action("toggle") if v else None),
                               (62, lambda v: self._step_action("pad", v)),
                               (63, lambda v: self._step_action("duplicate") if v else None),
                               (64, lambda v: self._step_action("navigate", v if v < 64 else v - 128)),
                               (65, lambda v: self._pad_bank_turn(v)),
                               (66, lambda v: self._keyboard_scale_turn(v)),
                               (67, lambda v: self._browser_action("close") if v else None),
                               (68, lambda v: self._browser_action("press", 49) if v else None),
                               (69, lambda v: self._create_midi_track() if v else None),
                               (70, lambda v: self._pattern_action("toggle") if v else None),
                               (71, lambda v: self._pattern_action("pad", v)),
                               (72, lambda v: self._pattern_action("navigate", v if v < 64 else v - 128)),
                               (73, lambda v: self._pattern_action("navigate", v if v < 64 else v - 128, True)),
                               (74, lambda v: self._pattern_action("toggle_scene") if v else None),
                               (75, lambda v: self._grid_action("grid_hold", bool(v))),
                               (76, lambda v: self._grid_action("grid_turn", v if v < 64 else v - 128)),
                               (77, lambda v: self._step_select_turn(v)),
                               (78, lambda v: self._scale_set(v)),
                               (79, lambda v: self._step_select_reset() if v else None),
                               (80, lambda v: self._device_action("toggle") if v else None),
                               (81, lambda v: self._device_action("navigate", v if v < 64 else v - 128)),
                               (82, lambda v: self._device_action("navigate", v if v < 64 else v - 128, True)),
                               (83, lambda v: self._device_action("turn", v if v < 64 else v - 128)),
                               (84, lambda v: self._device_action("turn", v if v < 64 else v - 128, True)),
                               (85, lambda v: self._device_action("close") if v else None),
                               (86, lambda v: self._navigation_action("toggle") if v else None),
                               (87, lambda v: self._navigation_action("navigate", v if v < 64 else v - 128)),
                               (88, lambda v: self._navigation_action("turn", v if v < 64 else v - 128)),
                               (89, lambda v: self._navigation_action("turn", v if v < 64 else v - 128, True)),
                               (90, lambda v: self._toggle_document_view() if v else None),
                               (91, lambda v: self._scale_octave(v))):
                button = ButtonElement(False, MIDI_CC_TYPE, 0, cc)
                button.add_value_listener(action)
                setattr(self, "_encoder_control_{}".format(cc), button)

        self.log_message("Mikro Companion loaded; 16-pad banking, view-based recording")

    def _create_midi_track(self):
        song = self.song()
        tracks = tuple(song.tracks)
        selected = song.view.selected_track
        index = tracks.index(selected) + 1 if selected in tracks else len(tracks)
        song.begin_undo_step()
        try:
            song.create_midi_track(index)
            song.view.selected_track = song.tracks[index]
        finally:
            song.end_undo_step()
        self._layout_changed()
        self.update_display()

    def _pattern_action(self, action, *args):
        try:
            if action in ("toggle", "toggle_scene"):
                self._navigation.close()
                self._devices.close()
                self._sequencer.grid_enabled = False
                self._step_return_grid = None
                scene_mode = action == "toggle_scene"
                if self._pattern.enabled and self._pattern.scene_mode == scene_mode:
                    if scene_mode and self._scene_previous_pattern:
                        self._pattern.release_all()
                        self._pattern.scene_mode = False
                        self._scene_previous_pattern = False
                        self._state_dirty = True
                        self.update_display()
                        return
                    self._pattern.close()
                    if self._pattern_previous is not None:
                        (self._keyboard.enabled, self._sequencer.enabled, self._selection.enabled,
                         self._mixer.enabled, self._mixer.tempo) = self._pattern_previous
                elif self._pattern.enabled:
                    self._scene_previous_pattern = scene_mode
                    self._pattern.release_all()
                    self._pattern.scene_mode = scene_mode
                else:
                    self._scene_previous_pattern = False
                    self._pattern_previous = (self._keyboard.enabled, self._sequencer.enabled,
                        self._selection.enabled, self._mixer.enabled, self._mixer.tempo)
                    self._sequencer.enabled = self._selection.enabled = False
                    self._mixer.enabled = self._mixer.tempo = self._browser.enabled = False
                    self.application().view.show_view("Session")
                    self._pattern.enter(scene_mode)
            else:
                getattr(self._pattern, action)(*args)
        except Exception as error:
            self._pattern.close()
            self.log_message("Mikro Companion Pattern unavailable: {}".format(error))
        self._state_dirty = True
        self.update_display()

    def refresh_state(self):
        super(MikroCompanion, self).refresh_state()
        if hasattr(self, "_socket"):
            self._feedback_track = None
            self._layout_dirty = True

    def _play(self, value):
        if value:
            self._navigation.play()

    def _navigation_action(self, action, *args):
        try:
            getattr(self._navigation, action)(*args)
        except Exception as error:
            self._navigation.close()
            self.log_message("Mikro Companion navigation unavailable: {}".format(error))
        self._state_dirty = True
        self.update_display()

    def _toggle_document_view(self):
        view = self.application().view
        view.show_view("Arranger" if view.is_view_visible("Session") else "Session")
        self._state_dirty = True
        self.update_display()

    def _device_action(self, action, *args):
        if action == "toggle":
            self._navigation.close()
        was_enabled = self._devices.enabled
        if action == "toggle" and not was_enabled:
            self._device_previous = (self._mixer.enabled, self._mixer.tempo)
        try:
            getattr(self._devices, action)(*args)
            if self._devices.enabled:
                self._mixer.enabled = self._mixer.tempo = False
                self._browser.enabled = False
            elif was_enabled:
                self._mixer.enabled, self._mixer.tempo = self._device_previous
        except Exception as error:
            self._devices.close()
            self.log_message("Mikro Companion device control unavailable: {}".format(error))
        self._state_dirty = True
        self.update_display()

    def _mixer_action(self, action, *args):
        if action in ("toggle", "toggle_tempo"):
            self._navigation.close()
            self._devices.close()
            self._sequencer.grid_enabled = False
            self._pattern.close()
            self._browser.enabled = False
        try:
            getattr(self._mixer, action)(*args)
        except Exception as error:
            self._mixer.enabled = False
            self._mixer.tempo = False
            self._browser.enabled = False
            self._sequencer.enabled = False
            self.log_message("Mikro Companion mixer unavailable: {}".format(error))
        self._layout_changed() if action == "navigate" else self._state_changed()
        self.update_display()

    def _browser_key(self, key):
        state = "MB1 {} {} ready".format(self._feedback_epoch, int(self._browser.enabled))
        self._socket.sendto(state.encode("ascii"), ("127.0.0.1", 39473))
        packet = "MBK1 {} {}".format(self._feedback_epoch, key)
        self._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))

    def _browser_action(self, action, *args):
        if action == "toggle":
            self._navigation.close()
            self._devices.close()
            self._sequencer.grid_enabled = False
            self._pattern.close()
        was_enabled = self._browser.enabled
        if action == "toggle" and not was_enabled:
            self._browser_previous = (self._mixer.enabled, self._mixer.tempo)
            self._mixer.enabled = self._mixer.tempo = False
        try:
            getattr(self._browser, action)(*args)
        except Exception as error:
            self._browser.enabled = False
            self.log_message("Mikro Companion browser unavailable: {}".format(error))
        if was_enabled and not self._browser.enabled:
            self._mixer.enabled, self._mixer.tempo = self._browser_previous
        self._state_dirty = True
        self.update_display()

    def _grid_action(self, action, *args):
        try:
            getattr(self._sequencer, action)(*args)
        except Exception as error:
            self._sequencer.grid_enabled = False
            self.log_message("Mikro Companion grid unavailable: {}".format(error))
        self._state_dirty = True
        self.update_display()

    def _step_action(self, action, *args):
        try:
            if action == "toggle":
                self._navigation.close()
                self._devices.close()
                self._sequencer.grid_enabled = False
                if self._sequencer.enabled and self._step_return_grid is not None:
                    self._sequencer.enabled = False
                    self._mixer.enabled = self._mixer.tempo = self._browser.enabled = False
                    self._pattern.enter(self._step_return_grid)
                    self._step_return_grid = None
                    self._state_dirty = True
                    self.update_display()
                    return
                if not self._sequencer.enabled:
                    self._step_return_grid = self._pattern.scene_mode if self._pattern.enabled else None
                self._pattern.close()
                self._mixer.enabled = self._mixer.tempo = self._browser.enabled = False
                self._selection.enabled = False
                pitch = self._selected_note()
                if pitch < 0:
                    pitch = next((n for n in self._keyboard.notes(self.song(), self._track) if n >= 0), 36) if self._keyboard.enabled else self._base_note()
                args = (pitch,)
            getattr(self._sequencer, action)(*args)
            if action == "toggle" and not self._sequencer.enabled and self._step_return_grid is not None:
                self._pattern.enter(self._step_return_grid)
                self._step_return_grid = None
        except Exception as error:
            self._sequencer.enabled = False
            self.log_message("Mikro Companion sequencer unavailable: {}".format(error))
        self._state_dirty = True
        self.update_display()

    def _restart(self, value):
        if value:
            self._navigation.cancel_start()
            song = self.song()
            song.start_playing()
            song.current_song_time = 0.0
            self._state_dirty = True

    def _loop(self, value):
        if value:
            song = self.song()
            song.loop = not song.loop
            self._state_dirty = True

    def _clip_editor_visible(self):
        visible = getattr(self.application().view, "is_view_visible", None)
        return bool(visible and visible("Detail/Clip"))

    def _events_action(self, action, *args):
        if action == "toggle":
            self._navigation.close()
            self._devices.close()
            self._sequencer.grid_enabled = False
            self._step_return_grid = None
            self._pattern.close()
            self._mixer.enabled = False
            self._mixer.tempo = False
            self._browser.enabled = False
            self._sequencer.enabled = False
        try:
            getattr(self._selection, action)(*args)
        except Exception as error:
            self._selection.enabled = False
            self.log_message("Mikro Companion selection unavailable: {}".format(error))
        self._state_dirty = True
        self._editor._next_check = 0
        self.update_display()

    def _edit(self, command):
        try:
            changed = self._editor.execute(command)
            if changed and self._selection.enabled and command in (1, 2, 13, 14, 15, 16):
                self._selection.edited()
            if command in (1, 2):
                self.log_message("Mikro Companion history command: {}; undo={}; redo={}".format(command, bool(self.song().can_undo), bool(self.song().can_redo)))
        except Exception as error:
            self.log_message("Mikro Companion edit unavailable: {}".format(error))
        self._state_dirty = True
        self._editor._next_check = 0
        self._selection._next_read = 0

    def _pad_mode(self, value):
        if value:
            self._navigation.close()
            self._devices.close()
            self._sequencer.grid_enabled = False
            self._step_return_grid = None
            self._pattern.close()
            self._mixer.enabled = False
            self._mixer.tempo = False
            self._browser.enabled = False
            self._sequencer.enabled = False
            self._keyboard.enabled = False
            self._layout_changed()
            self.update_display()

    def _keyboard_mode(self, value):
        if value and self._keyboard.available(self.song()):
            self._navigation.close()
            self._devices.close()
            self._sequencer.grid_enabled = False
            self._step_return_grid = None
            self._pattern.close()
            self._mixer.enabled = False
            self._mixer.tempo = False
            self._browser.enabled = False
            self._sequencer.enabled = False
            self._keyboard.enabled = True
            self._layout_changed()
            self.update_display()

    def _scale_turn(self, value):
        if value and self._keyboard.enabled:
            self._keyboard.adjust(self.song(), value if value < 64 else value - 128)
            self._layout_changed()
            self.update_display()

    def _scale_octave(self, value):
        if value and self._keyboard.enabled:
            self._keyboard.octave(self.song(), value if value < 64 else value - 128)
            self._layout_changed()
            self.update_display()

    def _reset_range(self, value):
        if self._sequencer.enabled or self._browser.enabled:
            return
        if not value:
            return
        self._reset_note_range()

    def _reset_note_range(self):
        if self._layout_dirty:
            self._rewire()
        if self._keyboard.enabled:
            self._keyboard.starts.pop(self._track, None)
        elif self._rack is not None:
            self._rack.view.drum_pads_scroll_position = 9
        else:
            self._fallback_bases.pop(self._track, None)
        self._layout_changed()
        self.update_display()

    def _scale_toggle(self, value):
        if value and self._keyboard.enabled and self._keyboard.available(self.song()):
            self.song().scale_mode = not self.song().scale_mode
            self._layout_changed()
            self.update_display()

    def _scale_set(self, value):
        if self._keyboard.enabled and self._keyboard.available(self.song()):
            enabled = value >= 64
            if self.song().scale_mode != enabled:
                self.song().scale_mode = enabled
                self._layout_changed()
                self.update_display()

    def _mix_track(self):
        song = self.song()
        track = song.view.selected_track
        return track if track in tuple(song.tracks) + tuple(getattr(song, "return_tracks", ())) else None

    def _mute(self, value):
        if value:
            track = self._mix_track()
            if track is not None and track in tuple(self.song().tracks):
                track.mute = not track.mute
                self._state_dirty = True

    def _solo(self, value):
        if value:
            song = self.song()
            track = self._mix_track()
            if track is None:
                return
            enabled = not track.solo
            if enabled and song.exclusive_solo:
                # Preserve an already soloed parent group so it cannot block this track.
                parents = []
                parent = getattr(track, "group_track", None)
                while parent is not None and parent not in parents:
                    parents.append(parent)
                    parent = getattr(parent, "group_track", None)
                for other in tuple(song.tracks) + tuple(getattr(song, "return_tracks", ())):
                    if other != track and other not in parents and other.solo:
                        other.solo = False
            track.solo = enabled
            self._state_dirty = True

    def _selected_note(self):
        if self._rack is not None:
            pad = getattr(self._rack.view, "selected_drum_pad", None)
            if pad is not None:
                return int(pad.note)
        return self._selected_notes.get((self._track, self._rack), -1)

    def _select_note(self, note):
        # CC values include zero: C-2 is a valid selection, not a release message.
        if self._layout_dirty:
            self._rewire()
        self._selected_notes[(self._track, self._rack)] = note
        if self._sequencer.enabled:
            self._sequencer.select_pitch(note)
        if self._rack is not None:
            for pad in self._rack.drum_pads:
                if int(pad.note) == note:
                    self._rack.view.selected_drum_pad = pad
                    break
        self._state_dirty = True
        self.update_display()

    def _follow(self, value):
        if value:
            view = self.song().view
            view.follow_song = not view.follow_song
            self._state_dirty = True

    def _tap(self, value):
        if value:
            self.song().tap_tempo()

    def _metronome(self, value):
        if value:
            song = self.song()
            song.metronome = not song.metronome
            self._state_dirty = True

    def _stop(self, value):
        if value:
            self.song().stop_playing()

    def _follow_recording_clip(self):
        if self._record_focus is None:
            return
        track, deadline = self._record_focus
        song = self.song()
        if time.monotonic() > deadline or not self._session_view_focused() or song.view.selected_track != track or not self._recording_active():
            self._record_focus = None
            return
        slots = tuple(getattr(track, "clip_slots", ()))
        index = int(getattr(track, "playing_slot_index", -1))
        candidates = [slots[index]] if 0 <= index < len(slots) else []
        requested = getattr(song.view, "highlighted_clip_slot", None)
        if requested in slots and requested not in candidates:
            candidates.append(requested)
        for slot in candidates:
            clip = slot.clip if slot.has_clip else None
            if clip is not None and (getattr(clip, "is_recording", False) or getattr(clip, "is_overdubbing", False)
                    or (getattr(clip, "is_playing", False) and song.session_record)):
                song.view.highlighted_clip_slot = slot
                song.view.detail_clip = clip
                self.application().view.show_view("Detail/Clip")
                self._record_focus = None
                return

    def _record(self, value):
        if not value:
            return
        try:
            song = self.song()
            if self._session_view_focused():
                start = not self._recording_active()
                track = getattr(getattr(song, "view", None), "selected_track", None)
                self._record_focus = (track, time.monotonic() + 30) if start and track is not None and (getattr(track, "arm", False) or getattr(track, "implicit_arm", False)) else None
                song.session_record = start
                mode = "Session"
            else:
                self._record_focus = None
                song.record_mode = not song.record_mode
                mode = "Arrangement"
            self._state_dirty = True
            self.log_message("Mikro Companion {} Record: {}".format(mode, self._recording_active()))
        except Exception as error:
            self.log_message("Mikro Companion recording unavailable: {}".format(error))
            self.show_message("Mikro Companion could not toggle recording")

    def _session_view_focused(self):
        return self.application().view.focused_document_view == "Session"

    def _recording_active(self):
        song = self.song()
        if self._session_view_focused():
            return bool(song.session_record) or (int(song.session_record_status) != 0)
        return bool(song.record_mode)

    def _base_note(self):
        if self._rack is not None:
            return int(self._rack.view.drum_pads_scroll_position) * 4
        return self._fallback_bases.get(self._track, 36)

    def _occupied_mask(self, base):
        if self._rack is None:
            return 65535
        mask = 0
        for pad in self._rack.drum_pads:
            index = int(pad.note) - base
            if 0 <= index < 16 and len(pad.chains):
                mask |= 1 << index
        return mask

    def _pad_activity(self, note):
        packet = "MP1 {} {}".format(self._feedback_epoch, note).encode("ascii")
        try:
            self._socket.sendto(packet, ("127.0.0.1", 39473))
            if not self._activity_seen:
                self.log_message("Mikro Companion pad activity received")
                self._activity_seen = True
        except OSError:
            pass

    def _watch_sampler_tree(self, parent, note, activity):
        self._listen(parent, "devices", self._layout_changed)
        for device in getattr(parent, "devices", []):
            if getattr(device, "playing_position_enabled", None) is not None:
                activity.watch(device, note)
            elif getattr(device, "can_have_chains", False):
                self._listen(device, "chains", self._layout_changed)
                for chain in device.chains:
                    self._watch_sampler_tree(chain, note, activity)

    def _keyboard_scale_turn(self, value):
        if value and self._keyboard.enabled:
            self._keyboard.turn(self.song(), value if value < 64 else value - 128)
            self._layout_changed()
            self.update_display()

    def _pad_bank_turn(self, value):
        if value and not self._keyboard.enabled and not self._sequencer.enabled and not self._selection.enabled:
            self._bank(value if value < 64 else value - 128)

    def _step_select_turn(self, value):
        if not value:
            return
        try:
            if self._layout_dirty:
                self._rewire()
            if not self._sequencer.enabled:
                return
            delta = value if value < 64 else value - 128
            if self._keyboard.enabled:
                self._keyboard.adjust(self.song(), delta)
            elif self._rack is not None:
                base = next_pad_bank(self._base_note(), delta)
                self._rack.view.drum_pads_scroll_position = base // 4
            else:
                self._fallback_bases[self._track] = next_pad_bank(self._base_note(), delta)
            self._layout_changed()
            self.update_display()
        except Exception as error:
            self.log_message("Mikro Companion sound range unavailable: {}".format(error))

    def _step_select_reset(self):
        if self._sequencer.enabled:
            self._reset_note_range()

    def _bank_down(self, value):
        if value:
            self._bank(-1)

    def _bank_up(self, value):
        if value:
            self._bank(1)

    def _bank(self, direction):
        try:
            if self._layout_dirty:
                self._rewire()
            if self._browser.enabled:
                self._browser_action("press", 37 if direction < 0 else 39)
                return
            if self._sequencer.enabled:
                self._step_action("navigate", direction)
                return
            if self._keyboard.enabled:
                return
            base = next_pad_bank(self._base_note(), direction)
            if self._rack is not None:
                self._rack.view.drum_pads_scroll_position = base // 4
                self._layout_dirty = True
            else:
                self._fallback_bases[self._track] = base
            self._state_dirty = True
            self.update_display()
        except Exception as error:
            self.log_message("Mikro Companion bank unavailable: {}".format(error))

    def _layout_changed(self):
        self._layout_dirty = True

    def _state_changed(self):
        self._state_dirty = True

    def _listen(self, obj, name, callback):
        if (obj, name, callback) in self._listeners:
            return
        add = getattr(obj, "add_" + name + "_listener", None)
        if add is not None:
            add(callback)
            self._listeners.append((obj, name, callback))

    def _remove_listeners(self):
        listeners, self._listeners = self._listeners, []
        for obj, name, callback in listeners:
            try:
                has_listener = getattr(obj, name + "_has_listener", None)
                if has_listener is None or has_listener(callback):
                    getattr(obj, "remove_" + name + "_listener")(callback)
            except Exception:
                # Deleted Live handles can raise Boost.Python.ArgumentError
                # (a TypeError), including during a listener existence check.
                # They must not prevent cleanup of the remaining live objects.
                pass

    def _rewire(self):
        self._remove_listeners()
        song = self.song()
        track = song.view.selected_track
        self._track = track
        if track != self._feedback_track:
            self._feedback_epoch += 1
            self._feedback_track = track
        selected = getattr(getattr(track, "view", None), "selected_device", None)
        racks = self._find_racks(track)
        preferred = [rack for rack, path in racks if selected is not None and selected in path]
        self._rack = preferred[0] if preferred else (racks[0][0] if racks else None)
        target = (track, self._rack)
        if target != self._last_target:
            if self._last_target is not None and self._last_target[0] == track:
                self._feedback_epoch += 1
            self.log_message("Mikro Companion pad target: track={}; rack={}".format(
                getattr(track, "name", ""),
                getattr(self._rack, "name", "") if self._rack is not None else "none (generic notes)"))
            self._last_target = target
        valid_tracks = tuple(song.tracks) + tuple(getattr(song, "return_tracks", ()))
        self._selected_notes = {key: note for key, note in self._selected_notes.items() if key[0] in valid_tracks and (key[0] != track or key[1] is None or key[1] in [rack for rack, _ in racks])}
        self._fallback_bases = {t: base for t, base in self._fallback_bases.items() if t in song.tracks}
        self._listen(song.view, "selected_track", self._layout_changed)
        self._listen(song, "loop", self._state_changed)
        self._listen(song, "metronome", self._state_changed)
        self._listen(song, "can_undo", self._state_changed)
        self._listen(song, "can_redo", self._state_changed)
        for name in ("root_note", "scale_name", "scale_intervals", "scale_mode"):
            self._listen(song, name, self._layout_changed)
        self._listen(song.view, "follow_song", self._state_changed)
        track_view = getattr(track, "view", None)
        if track_view is not None:
            self._listen(track_view, "selected_device", self._layout_changed)
        if self._rack is not None:
            self._listen(self._rack.view, "drum_pads_scroll_position", self._layout_changed)
            self._listen(self._rack.view, "selected_drum_pad", self._state_changed)
            self._listen(self._rack, "drum_pads", self._layout_changed)
            self._listen(self._rack, "chains", self._layout_changed)
            for pad in self._rack.drum_pads:
                self._listen(pad, "chains", self._state_changed)
            activity = SamplerActivity(self._listen, self._pad_activity,
                                       lambda error: self.log_message("Mikro Companion sampler activity unavailable: " + error))
            visible = set(self._keyboard.notes(song, track)) if self._keyboard.enabled else set(range(self._base_note(), self._base_note() + 16))
            for pad in self._rack.drum_pads:
                if pad.note in visible:
                    self._listen(pad, "chains", self._layout_changed)
                    for chain in pad.chains:
                        self._watch_sampler_tree(chain, int(pad.note), activity)
        self._listen(song, "is_playing", self._state_changed)
        self._listen(song, "session_record", self._state_changed)
        self._listen(song, "session_record_status", self._state_changed)
        self._listen(song, "record_mode", self._state_changed)
        self._listen(self.application().view, "focused_document_view", self._state_changed)
        self._listen(track, "color", self._state_changed)
        self._listen(track, "name", self._state_changed)
        volume = self._mixer.parameter()
        if volume is not None:
            self._listen(volume, "value", self._state_changed)
            self._listen(volume, "is_enabled", self._state_changed)
        if self._mix_track() is not None:
            self._listen(track, "mute", self._state_changed)
            self._listen(track, "solo", self._state_changed)
        self._layout_dirty = False
        self._state_dirty = True

    def _find_racks(self, parent, path=()):
        # Instrument Rack presets can wrap the Drum Rack several chains deep.
        # Stop at the first Drum Rack: its inner racks share the outer pad grid.
        self._listen(parent, "devices", self._layout_changed)
        result = []
        for device in getattr(parent, "devices", []):
            device_path = path + (device,)
            if getattr(device, "can_have_drum_pads", False):
                result.append((device, device_path))
            elif getattr(device, "can_have_chains", False):
                self._listen(device, "chains", self._layout_changed)
                view = device.view
                self._listen(view, "selected_chain", self._layout_changed)
                chains = list(device.chains)
                selected_chain = view.selected_chain
                if selected_chain in chains:
                    chains.remove(selected_chain)
                    chains.insert(0, selected_chain)
                for chain in chains:
                    result.extend(self._find_racks(chain, device_path))
        return result

    def update_display(self):
        super(MikroCompanion, self).update_display()
        now = time.monotonic()
        if now < self._retry_at:
            return
        try:
            if self._layout_dirty:
                self._rewire()
            self._follow_recording_clip()
            song = self.song()
            if now >= self._ownership_at or song.view.selected_track != self._ownership_track:
                self._ownership_at = now + 0.5
                self._ownership_track = song.view.selected_track
                ownership = inspect_song(song)
                if ownership != self._ownership_state:
                    self.log_message('Mikro Companion NI ownership: selected={}, instances={}, uncertain={}'.format(*ownership))
                self._ownership_state = ownership
            native, instances, uncertain = self._ownership_state
            ownership_packet = 'MO1 {} {} {} {} ready'.format(self._feedback_epoch, int(native), instances, int(uncertain))
            self._socket.sendto(ownership_packet.encode('ascii'), ('127.0.0.1', 39473))
            sampled = time.perf_counter()
            packet = "MR2 {} {:.6f} {:.6f} {} {:.9f} ready".format(self._feedback_epoch,
                float(getattr(song, "tempo", 120.0)), float(getattr(song, "current_song_time", 0.0)), int(song.is_playing), sampled)
            self._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
            packet = self._pattern.packet(self._feedback_epoch)
            if packet != self._last_pattern_packet or self._state_dirty or now - self._last_sent >= 0.5:
                self._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
                self._last_pattern_packet = packet
            if hasattr(self.song().view, "detail_clip"):
                try:
                    packet = self._sequencer.packet(self._feedback_epoch, now)
                except Exception as error:
                    if self._sequencer.enabled:
                        self.log_message("Mikro Companion sequencer unavailable: {}".format(error))
                    self._sequencer.enabled = False
                    packet = "MS1 {} 0 0 36 0 1 0 0 -1 17 0 0 -1 0 ready".format(self._feedback_epoch)
                if not self._sequencer.enabled and self._step_return_grid is not None:
                    self._mixer.enabled = self._mixer.tempo = self._browser.enabled = False
                    self._pattern.enter(self._step_return_grid)
                    self._step_return_grid = None
                    pattern_packet = self._pattern.packet(self._feedback_epoch)
                    self._socket.sendto(pattern_packet.encode("ascii"), ("127.0.0.1", 39473))
                    self._last_pattern_packet = pattern_packet
                    self._state_dirty = True
                if packet != self._last_step_packet or self._state_dirty or now - self._last_sent >= 0.5:
                    self._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
                    self._last_step_packet = packet
            if hasattr(self.song().view, "detail_clip"):
                packet = self._sequencer.grid_packet(self._feedback_epoch)
                if packet != self._last_grid_packet or self._state_dirty or now - self._last_sent >= 0.5:
                    self._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
                    self._last_grid_packet = packet
            if hasattr(self.application().view, "show_view"):
                packet = "MB1 {} {} ready".format(self._feedback_epoch, int(self._browser.enabled))
                if packet != self._last_browser_packet or self._state_dirty or now - self._last_sent >= 0.5:
                    self._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
                    self._last_browser_packet = packet
            if hasattr(self.song(), "tempo"):
                packet = "MCU1 {} {} {:.2f} ready".format(self._feedback_epoch, int(self._mixer.tempo), self.song().tempo)
                self._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
            try:
                device_packet = self._devices.packet(self._feedback_epoch)
                self._last_device_error = None
            except Exception as error:
                self._devices.close()
                device_packet = "MDEV1 {} 0 0 0 0 0 0 - - - ready".format(self._feedback_epoch)
                if str(error) != self._last_device_error:
                    self.log_message("Mikro Companion device state unavailable: {}".format(error))
                    self._last_device_error = str(error)
            if device_packet != self._last_device_packet or self._state_dirty or now - self._last_sent >= 0.5:
                self._socket.sendto(device_packet.encode("ascii"), ("127.0.0.1", 39473))
                self._last_device_packet = device_packet
            try:
                navigation_packet = self._navigation.packet(self._feedback_epoch)
                self._last_navigation_error = None
            except Exception as error:
                self._navigation.close()
                navigation_packet = "MNAV1 {} 0 0 - ready".format(self._feedback_epoch)
                if str(error) != self._last_navigation_error:
                    self.log_message("Mikro Companion navigation state unavailable: {}".format(error))
                    self._last_navigation_error = str(error)
            if navigation_packet != self._last_navigation_packet or self._state_dirty or now - self._last_sent >= 0.5:
                self._socket.sendto(navigation_packet.encode("ascii"), ("127.0.0.1", 39473))
                self._last_navigation_packet = navigation_packet
            mixer_packet = self._mixer.packet(self._feedback_epoch)
            if mixer_packet is not None and (mixer_packet != self._last_mixer_packet or self._state_dirty or now - self._last_sent >= 0.5):
                self._socket.sendto(mixer_packet.encode("ascii"), ("127.0.0.1", 39473))
                self._last_mixer_packet = mixer_packet
            if hasattr(self.song().view, "detail_clip"):
                try:
                    self._selection.sync(now)
                    pitches = self._keyboard.notes(self.song(), self._track) if self._keyboard.enabled else range(self._base_note(), self._base_note() + 16)
                    present, selected = self._selection.masks(pitches)
                    selection = (self._feedback_epoch, int(self._selection.available), int(self._selection.enabled), present, selected, int(self._selection.can_erase()))
                    self._last_selection_error = None
                except Exception as error:
                    self._selection.enabled = False
                    selection = (self._feedback_epoch, 0, 0, 0, 0, 0)
                    if str(error) != self._last_selection_error:
                        self.log_message("Mikro Companion selection state unavailable: {}".format(error))
                    self._last_selection_error = str(error)
                if selection != self._last_selection_state or self._state_dirty or now - self._last_sent >= 0.5:
                    packet = "MV2 {} {} {} {} {} {} ready".format(*selection)
                    self._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
                    self._last_selection_state = selection
            if hasattr(self.song(), "can_undo"):
                try:
                    edit_mask = self._editor.availability(now)
                    history = (bool(self.song().can_undo), bool(self.song().can_redo))
                    if self._editor.shift and history != self._last_history:
                        self.log_message("Mikro Companion history state: undo={}; redo={}".format(*history))
                        self._last_history = history
                    if edit_mask != self._last_edit_mask or self._state_dirty or now - self._last_sent >= 0.5:
                        packet = "ME1 {} {} ready".format(self._feedback_epoch, edit_mask)
                        self._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
                        self._last_edit_mask = edit_mask
                    self._last_edit_error = None
                except Exception as error:
                    message = str(error)
                    if message != self._last_edit_error:
                        self.log_message("Mikro Companion edit state unavailable: {}".format(message))
                    self._last_edit_error = message
                    packet = "ME1 {} {} ready".format(self._feedback_epoch, int(self.song().can_undo) | (int(self.song().can_redo) << 1))
                    self._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
            if self._state_dirty or now - self._last_sent >= 0.5:
                song = self.song()
                color = pad_color(getattr(self._track, "color", 0x0000FF))
                base = self._base_note()
                mask = self._occupied_mask(base)
                mix = self._mix_track()
                normal = self._track in tuple(song.tracks)
                notes = normal and getattr(self._track, "has_midi_input", True)
                packet = "MN1 {} {} {} ready".format(self._feedback_epoch, int(notes), int(normal))
                self._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
                state = "MC7 {} {} {} {} {} {} {} {} {} {} {} {} ready".format(
                    mask, int(song.is_playing), int(self._recording_active()), color, base, self._feedback_epoch, int(song.metronome), int(song.view.follow_song),
                    self._selected_note(), int(getattr(mix, "mute", False)) if mix is not None else 0, int(getattr(mix, "solo", False)) if mix is not None else 0, int(mix is not None)
                )
                if hasattr(self._track, "name"):
                    name = ''.join(c for c in self._track.name if c.isprintable())[:64] or 'Track'
                    tracks = tuple(song.tracks)
                    returns = tuple(getattr(song, 'return_tracks', ()))
                    if self._track in tracks:
                        name = numbered_track_name(name, str(tracks.index(self._track) + 1))
                    elif self._track in returns:
                        index = returns.index(self._track)
                        name = numbered_track_name(name, chr(65 + index) if index < 26 else 'R{}'.format(index + 1))
                    encoded = base64.b64encode(name.encode('utf-8')).decode('ascii')
                    packet = "MD1 {} {} ready".format(self._feedback_epoch, encoded)
                    self._socket.sendto(packet.encode('ascii'), ('127.0.0.1', 39473))
                keyboard = self._keyboard.packet(song, self._track, self._feedback_epoch)
                if keyboard is not None:
                    self._socket.sendto(keyboard.encode("ascii"), ("127.0.0.1", 39473))
                if hasattr(song, "loop"):
                    packet = "MT1 {} {} ready".format(self._feedback_epoch, int(song.loop))
                    self._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
                self._socket.sendto(state.encode("ascii"), ("127.0.0.1", 39473))
                self._last_sent = now
                self._state_dirty = False
                layout = (mask, color, base)
                if layout != self._last_layout:
                    notes = [base + i for i in range(16) if mask & (1 << i)]
                    self.log_message("Mikro Companion notes: {}; pad color: {}".format(notes, color))
                    self._last_layout = layout
            if self._keyboard.enabled:
                notes = [n for n in self._keyboard.notes(self.song(), self._track) if n >= 0]
                if notes:
                    self._clip_activity.update(self.song(), self._track, min(notes), max(notes) - min(notes) + 1)
            else:
                self._clip_activity.update(self.song(), self._track, self._base_note())
            if self._last_error is not None:
                self.log_message("Mikro Companion state recovered")
            self._last_error = None
        except Exception as error:
            # No heartbeat on a failed state read: the companion releases notes.
            message = "{}: {}".format(type(error).__name__, error)
            if message != self._last_error:
                self.log_message("Mikro Companion state unavailable: {}".format(message))
            self._last_error = message
            self._retry_at = now + 1.0
            self._state_dirty = True
            self._layout_dirty = True

    def disconnect(self):
        self._pattern.close()
        self._clip_activity.disconnect()
        self._remove_listeners()
        self._socket.close()
        super(MikroCompanion, self).disconnect()
