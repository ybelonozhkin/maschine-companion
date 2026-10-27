import socket
import time

from _Framework.ControlSurface import ControlSurface

from .model import next_pad_bank
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
from .controls import register_controls
from .feedback import publish_state


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
        register_controls(self)

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
            if action not in ('duplicate', 'erase'):
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
            if command == 9 and self._selection.enabled:
                changed = self._selection.can_erase()
                self._selection.erase()
            else:
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
        publish_state(self)

    def disconnect(self):
        try:
            self._pattern.close()
        finally:
            try:
                self._clip_activity.disconnect()
            finally:
                try:
                    self._remove_listeners()
                finally:
                    try:
                        self._socket.close()
                    finally:
                        super(MikroCompanion, self).disconnect()
