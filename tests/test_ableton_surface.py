import contextlib
import importlib.util
import pathlib
import sys
import types
import unittest
from unittest.mock import Mock, patch


class BaseSurface:
    def __init__(self, song):
        self._song = song
        self._application = LiveNode(view=LiveNode(focused_document_view="Session"))
        self._c_instance = Mock()
        self.set_feedback_channels = Mock()
        self.set_controlled_track = Mock()
        self.release_controlled_track = Mock()
        self._send_midi = Mock()
        self.set_pad_translations = Mock()
        self.request_rebuild_midi_map = Mock()
        self.messages = []

    def song(self):
        return self._song

    def application(self):
        return self._application

    def component_guard(self):
        return contextlib.nullcontext()

    def log_message(self, message):
        self.messages.append(message)

    def show_message(self, message):
        self.messages.append(message)

    def update_display(self):
        pass

    def refresh_state(self):
        pass

    def build_midi_map(self, midi_map_handle):
        pass

    def disconnect(self):
        pass


class Button:
    def __init__(self, *args):
        pass

    def add_value_listener(self, callback):
        pass


directory = pathlib.Path(__file__).parents[1] / "ableton" / "MikroCompanion"
package = types.ModuleType("mikro_surface_test")
package.__path__ = [str(directory)]
framework = types.ModuleType("_Framework")
base = types.ModuleType("_Framework.ControlSurface")
base.ControlSurface = BaseSurface
button = types.ModuleType("_Framework.ButtonElement")
button.ButtonElement = Button
control = types.ModuleType("_Framework.InputControlElement")
control.MIDI_CC_TYPE = 1
with patch.dict(sys.modules, {
    "mikro_surface_test": package, "_Framework": framework,
    "_Framework.ControlSurface": base, "_Framework.ButtonElement": button,
    "_Framework.InputControlElement": control,
}):
    spec = importlib.util.spec_from_file_location("mikro_surface_test.surface", directory / "surface.py")
    surface_module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(surface_module)


class LiveNode:
    """A deleted Live handle raises TypeError from its listener binding."""

    def __init__(self, **values):
        if values.get("can_have_drum_pads") and "drum_pads" not in values:
            values["drum_pads"] = [LiveNode(note=note, chains=[LiveNode()]) for note in range(128)]
        self.__dict__.update(values)
        self.callbacks = {}
        self.deleted = False

    def __getattr__(self, name):
        if name.startswith("add_") and name.endswith("_listener"):
            return lambda callback: self.callbacks.setdefault(name[4:-9], []).append(callback)
        if name.startswith("remove_") and name.endswith("_listener"):
            def remove(callback):
                if self.deleted:
                    raise TypeError("Boost.Python.ArgumentError: deleted Live handle")
                self.callbacks[name[7:-9]].remove(callback)
            return remove
        if name.endswith("_has_listener"):
            def has(callback):
                if self.deleted:
                    raise TypeError("Boost.Python.ArgumentError: deleted Live handle")
                return callback in self.callbacks.get(name[:-13], [])
            return has
        raise AttributeError(name)


class SurfaceTests(unittest.TestCase):
    def test_track_display_name_follows_unicode_rename(self):
        song = self.song_with_track()
        song.view.selected_track.name = 'Analog'
        surface, connection = self.make_surface(song)
        surface.update_display()
        import base64
        packets = [call.args[0].decode('ascii') for call in connection.sendto.call_args_list]
        self.assertIn('MD1 {} MSBBbmFsb2c= ready'.format(surface._feedback_epoch), packets)
        name = '\u0411\u0430\u0441\nLead'
        song.view.selected_track.name = name
        song.view.selected_track.callbacks['name'][0]()
        surface.update_display()
        encoded = base64.b64encode(('1 ' + name.replace('\n', '')).encode('utf-8')).decode('ascii')
        self.assertTrue(any(call.args[0] == 'MD1 {} {} ready'.format(surface._feedback_epoch, encoded).encode('ascii') for call in connection.sendto.call_args_list))
    def test_new_midi_track_is_inserted_after_selection_and_selected(self):
        song = self.song_with_track()
        first = song.view.selected_track
        second = LiveNode()
        song.tracks.append(second)
        song.begin_undo_step, song.end_undo_step = Mock(), Mock()
        def create(index):
            song.tracks.insert(index, LiveNode(name="MIDI", has_midi_input=True))
        song.create_midi_track = Mock(side_effect=create)
        surface, _ = self.make_surface(song)
        surface._create_midi_track()
        song.create_midi_track.assert_called_once_with(1)
        self.assertIs(song.view.selected_track, song.tracks[1])
        self.assertIs(song.tracks[0], first)
        self.assertIs(song.tracks[2], second)
        song.begin_undo_step.assert_called_once_with()
        song.end_undo_step.assert_called_once_with()
        song.view.selected_track = LiveNode(name="Master")
        surface._create_midi_track()
        self.assertEqual(song.create_midi_track.call_args.args, (3,))
        self.assertIs(song.view.selected_track, song.tracks[3])

    def make_surface(self, song):
        fake_socket = Mock()
        with patch.object(surface_module.socket, "socket", return_value=fake_socket):
            surface = surface_module.MikroCompanion(song)
        return surface, fake_socket

    def song_with_track(self, color=0x0000FF):
        track = LiveNode(color=color, arm=False, implicit_arm=False, mute=False, solo=False)
        return LiveNode(view=LiveNode(selected_track=track, follow_song=False), is_playing=False,
                        session_record=False, session_record_status=0, record_mode=False, metronome=False, tracks=[track])

    def test_navigate_preserves_pad_and_encoder_modes_and_view_toggle(self):
        song = self.song_with_track()
        song.get_current_beats_song_time = Mock(return_value="1.1.1.0")
        surface, connection = self.make_surface(song)
        surface._keyboard.enabled = True
        surface._mixer.enabled = True
        surface._navigation_action("toggle")
        self.assertTrue(surface._navigation.enabled)
        self.assertTrue(surface._keyboard.enabled)
        self.assertTrue(surface._mixer.enabled)
        surface._application.view.is_view_visible = Mock(return_value=True)
        surface._application.view.show_view = Mock()
        surface._toggle_document_view()
        surface._application.view.show_view.assert_called_with("Arranger")
        self.assertTrue(surface._navigation.enabled)
        self.assertTrue(surface._keyboard.enabled)
        surface._application.view.is_view_visible.return_value = False
        surface._toggle_document_view()
        surface._application.view.show_view.assert_called_with("Session")
        surface._navigation_action("navigate", 1)
        surface._navigation_action("toggle")
        self.assertFalse(surface._navigation.enabled)
        self.assertTrue(surface._mixer.enabled)
        self.assertEqual(surface._navigation.page, 1)
        surface._navigation_action("toggle")
        surface._pad_mode(127)
        self.assertFalse(surface._navigation.enabled)

    def test_volume_focus_restores_keyboard_and_tracks_change_without_arm(self):
        class Volume(LiveNode):
            def str_for_value(self, value):
                return "{:.2f} dB".format(80 * value * value - 45)

            def __str__(self):
                return self.str_for_value(self.value)
        song = self.song_with_track()
        song.root_note, song.scale_mode, song.scale_name = 0, True, "Major"
        song.scale_intervals = (0, 2, 4, 5, 7, 9, 11)
        first = song.view.selected_track
        first.mixer_device = LiveNode(volume=Volume(value=0.5, min=0, max=1, is_enabled=True))
        second = LiveNode(color=0, arm=False, mixer_device=LiveNode(volume=Volume(value=0.5, min=0, max=1, is_enabled=True)))
        song.tracks.append(second)
        surface, connection = self.make_surface(song)
        surface._keyboard_mode(127)
        surface._mixer_action("toggle")
        self.assertTrue(surface._mixer.enabled)
        surface._mixer_action("turn", 1)
        self.assertEqual(str(first.mixer_device.volume), "-24.50 dB")
        surface._mixer_action("navigate", 1)
        self.assertIs(song.view.selected_track, second)
        self.assertTrue(surface._mixer.enabled)
        self.assertFalse(first.arm or second.arm)
        surface._mixer_action("toggle")
        self.assertFalse(surface._mixer.enabled)
        self.assertTrue(any(call.args[0].startswith(b"MX1 ") for call in connection.sendto.call_args_list))

    def test_step_and_browser_encoder_focus_and_target_changes(self):
        song = self.song_with_track()
        track = song.view.selected_track
        song.tempo = 120.0
        song.view.detail_clip = LiveNode(is_midi_clip=True, canonical_parent=track,
            looping=True, loop_start=0.0, loop_end=4.0, is_playing=False, color=0xFF0000)
        song.view.detail_clip.get_all_notes_extended = Mock(return_value=[])
        song.view.detail_clip.get_selected_notes_extended = Mock(return_value=[])
        surface, connection = self.make_surface(song)
        surface._application.view.is_view_visible = Mock(return_value=True)
        surface._application.view.show_view = Mock()
        surface._application.view.focus_view = Mock()
        surface._select_note(38)
        surface._step_action("toggle")
        self.assertTrue(surface._sequencer.enabled)
        self.assertEqual(surface._sequencer.pitch, 38)
        surface._select_note(40)
        self.assertEqual(surface._sequencer.pitch, 40)
        self.assertTrue(surface._sequencer.enabled)
        self.assertTrue(any(call.args[0].startswith(b"MS1 ") for call in connection.sendto.call_args_list))
        surface._mixer_action("toggle_tempo")
        self.assertTrue(surface._mixer.tempo)
        self.assertTrue(surface._sequencer.enabled)
        surface._browser_action("toggle")
        self.assertTrue(surface._browser.enabled)
        self.assertFalse(surface._mixer.tempo)
        surface._browser_action("close")
        self.assertTrue(surface._mixer.tempo)
        surface._browser_action("toggle")
        self.assertFalse(surface._mixer.tempo)
        keys = Mock()
        surface._browser._send_key = keys
        surface._bank(1)
        keys.assert_called_with(39)
        surface._pad_mode(127)
        self.assertFalse(surface._sequencer.enabled)
        self.assertFalse(surface._browser.enabled)

    def test_encoder_bank_and_keyboard_scale_use_distinct_commands(self):
        song = self.song_with_track()
        song.root_note, song.scale_mode, song.scale_name = 0, True, "Major"
        song.scale_intervals = (0, 2, 4, 5, 7, 9, 11)
        surface, _ = self.make_surface(song)
        surface._pad_bank_turn(1)
        self.assertEqual(surface._base_note(), 52)
        surface._pad_bank_turn(127)
        self.assertEqual(surface._base_note(), 36)
        surface._keyboard_mode(127)
        with patch.object(surface._keyboard, "turn") as turn:
            surface._keyboard_scale_turn(1)
            turn.assert_called_with(song, 1)
            surface._keyboard_scale_turn(127)
            turn.assert_called_with(song, -1)
        self.assertEqual(song.root_note, 0)
        self.assertTrue(song.scale_mode)
        surface._pad_bank_turn(1)
        self.assertEqual(surface._base_note(), 36)
        surface._pad_mode(127)
        with patch.object(surface._keyboard, "turn") as turn:
            surface._keyboard_scale_turn(1)
            turn.assert_not_called()

    def test_step_sound_encoder_respects_keyboard_scale_without_changing_mode_or_page(self):
        song = self.song_with_track()
        song.root_note, song.scale_name, song.scale_mode = 0, "Major", True
        song.scale_intervals = (0, 2, 4, 5, 7, 9, 11)
        track = song.view.selected_track
        song.view.detail_clip = LiveNode(is_midi_clip=True, canonical_parent=track, looping=True,
                                        loop_start=0.0, loop_end=8.0, get_all_notes_extended=Mock(return_value=[]))
        surface, connection = self.make_surface(song)
        surface.application().view.is_view_visible = lambda name: name == "Detail/Clip"
        surface._keyboard_mode(127)
        surface._step_action("toggle")
        surface._sequencer.page = 1
        surface._step_select_turn(1)
        self.assertTrue(surface._sequencer.enabled)
        self.assertTrue(surface._keyboard.enabled)
        self.assertEqual((surface._sequencer.page, surface._sequencer.pitch), (1, 36))
        self.assertEqual(surface._keyboard.notes(song, track)[:4], (37, 39, 41, 42))
        self.assertEqual(song.root_note, 1)
        surface._select_note(41)
        self.assertEqual(surface._sequencer.pitch, 41)
        self.assertEqual(surface._sequencer.page, 1)
        song.scale_mode = False
        self.assertEqual(surface._keyboard.notes(song, track)[:4], (37, 38, 39, 40))

    def test_step_sound_encoder_scrolls_drum_rack_without_turning_pages(self):
        song = self.song_with_track()
        track = song.view.selected_track
        rack = LiveNode(can_have_drum_pads=True, view=LiveNode(drum_pads_scroll_position=9))
        track.devices = [rack]
        song.view.detail_clip = LiveNode(is_midi_clip=True, canonical_parent=track, looping=True,
                                        loop_start=0.0, loop_end=8.0, get_all_notes_extended=Mock(return_value=[]))
        surface, connection = self.make_surface(song)
        surface.application().view.is_view_visible = lambda name: name == "Detail/Clip"
        surface._step_action("toggle")
        surface._sequencer.page = 1
        surface._step_select_turn(1)
        self.assertIs(surface._rack, rack)
        self.assertEqual(rack.view.drum_pads_scroll_position, 13)
        self.assertEqual(surface._sequencer.page, 1)
        self.assertTrue(surface._sequencer.enabled)
        surface._step_select_turn(127)
        self.assertEqual(rack.view.drum_pads_scroll_position, 9)
        surface._step_select_turn(1)
        surface._step_select_reset()
        self.assertEqual(rack.view.drum_pads_scroll_position, 9)
        self.assertEqual(surface._sequencer.page, 1)

    def test_step_pad_mode_uses_banks_for_a_melodic_instrument_and_reset_preserves_keyboard(self):
        song = self.song_with_track()
        song.root_note, song.scale_name, song.scale_mode = 2, "Major", True
        song.scale_intervals = (0, 2, 4, 5, 7, 9, 11)
        track = song.view.selected_track
        song.view.detail_clip = LiveNode(is_midi_clip=True, canonical_parent=track, looping=True,
                                        loop_start=0.0, loop_end=8.0, get_all_notes_extended=Mock(return_value=[]))
        surface, connection = self.make_surface(song)
        surface.application().view.is_view_visible = lambda name: name == "Detail/Clip"
        surface._keyboard.starts[track] = 62
        surface._step_action("toggle")
        surface._sequencer.page = 1
        surface._step_select_turn(1)
        self.assertEqual(surface._base_note(), 52)
        self.assertEqual((song.root_note, surface._keyboard.start(song, track)), (2, 62))
        surface._step_select_reset()
        self.assertEqual(surface._base_note(), 36)
        self.assertEqual(surface._sequencer.page, 1)
        self.assertTrue(surface._sequencer.enabled)
        self.assertEqual(surface._keyboard.start(song, track), 62)

    def test_step_keyboard_mode_uses_scale_even_with_drum_rack_and_resets_only_octave(self):
        song = self.song_with_track()
        song.root_note, song.scale_name, song.scale_mode = 1, "Major", True
        song.scale_intervals = (0, 2, 4, 5, 7, 9, 11)
        track = song.view.selected_track
        rack = LiveNode(can_have_drum_pads=True, view=LiveNode(drum_pads_scroll_position=13))
        track.devices = [rack]
        song.view.detail_clip = LiveNode(is_midi_clip=True, canonical_parent=track, looping=True,
                                        loop_start=0.0, loop_end=8.0, get_all_notes_extended=Mock(return_value=[]))
        surface, connection = self.make_surface(song)
        surface.application().view.is_view_visible = lambda name: name == "Detail/Clip"
        surface._keyboard_mode(127)
        surface._keyboard.starts[track] = 61
        surface._step_action("toggle")
        surface._sequencer.page = 1
        surface._step_select_turn(1)
        self.assertEqual((song.root_note, surface._keyboard.start(song, track)), (2, 62))
        self.assertEqual(rack.view.drum_pads_scroll_position, 13)
        surface._step_select_reset()
        self.assertEqual(surface._keyboard.start(song, track), 38)
        self.assertEqual((song.root_note, song.scale_name, song.scale_mode), (2, "Major", True))
        self.assertEqual(rack.view.drum_pads_scroll_position, 13)
        self.assertEqual(surface._sequencer.page, 1)
        self.assertTrue(surface._sequencer.enabled)

    def test_restart_loop_and_keyboard_menu(self):
        song = self.song_with_track()
        song.start_playing = Mock()
        song.current_song_time = 16.0
        song.loop = False
        song.root_note, song.scale_mode, song.scale_name = 0, True, "Major"
        song.scale_intervals = (0, 2, 4, 5, 7, 9, 11)
        surface, _ = self.make_surface(song)
        surface._restart(0)
        song.start_playing.assert_not_called()
        surface._restart(127)
        song.start_playing.assert_called_once_with()
        self.assertEqual(song.current_song_time, 0)
        surface._loop(127)
        surface._loop(0)
        self.assertTrue(song.loop)
        surface._loop(127)
        self.assertFalse(song.loop)
        surface._keyboard_mode(127)
        surface._scale_turn(1)
        self.assertEqual(song.root_note, 1)
        surface._scale_octave(1)
        self.assertEqual((song.root_note, surface._keyboard.start(song, surface._track)), (1, 49))
        surface._scale_octave(127)
        self.assertEqual((song.root_note, surface._keyboard.start(song, surface._track)), (1, 37))
        with patch.object(surface._keyboard, "turn") as turn:
            surface._keyboard_scale_turn(1)
            turn.assert_called_with(song, 1)
        surface._scale_toggle(127)
        self.assertFalse(song.scale_mode)
        surface._scale_turn(1)
        self.assertEqual(surface._keyboard.start(song, surface._track), 38)

    def test_events_from_step_selects_notes_with_previous_pitch_layout(self):
        for keyboard_mode in (False, True):
            song = self.song_with_track()
            song.root_note, song.scale_name, song.scale_mode = 0, "Major", True
            song.scale_intervals = (0, 2, 4, 5, 7, 9, 11)
            clip = LiveNode(is_midi_clip=True, canonical_parent=song.view.selected_track,
                            looping=True, loop_start=0.0, loop_end=4.0, color=0xFF0000,
                            is_playing=False)
            clip.get_all_notes_extended = Mock(return_value=[])
            clip.get_selected_notes_extended = Mock(return_value=[])
            song.view.detail_clip = clip
            surface, connection = self.make_surface(song)
            surface._application.view.is_view_visible = Mock(return_value=True)
            if keyboard_mode:
                surface._keyboard_mode(127)
            surface._step_action("toggle")
            self.assertTrue(surface._sequencer.enabled)
            surface._events_action("toggle")
            self.assertFalse(surface._sequencer.enabled)
            self.assertTrue(surface._selection.enabled)
            self.assertEqual(surface._keyboard.enabled, keyboard_mode)
            self.assertIs(surface._selection.clip, clip)

    def test_events_uses_visible_clip_without_switching_live_views(self):
        song = self.song_with_track()
        track = song.view.selected_track
        notes = [LiveNode(note_id=1, pitch=36, start_time=0.0), LiveNode(note_id=2, pitch=36, start_time=1.0)]
        clip = LiveNode(is_midi_clip=True, canonical_parent=LiveNode(canonical_parent=track))
        clip.get_all_notes_extended = Mock(return_value=notes)
        clip.get_selected_notes_extended = Mock(return_value=[])
        clip.deselect_all_notes = Mock()
        clip.select_notes_by_id = Mock()
        song.view.detail_clip = clip
        surface, connection = self.make_surface(song)
        surface._application.view.is_view_visible = Mock(return_value=True)
        surface._events_action("toggle")
        self.assertTrue(surface._selection.enabled)
        surface._events_action("pad", 36)
        clip.select_notes_by_id.assert_called_with((1, 2))
        surface._events_action("turn", 1)
        clip.select_notes_by_id.assert_called_with((1,))
        surface._application.view.is_view_visible.return_value = False
        surface.update_display()
        self.assertFalse(surface._selection.enabled)
        self.assertTrue(any(call.args[0].startswith(b"MV2 ") for call in connection.sendto.call_args_list))

    def test_keyboard_pages_keep_drum_bank_and_keyboard_octave(self):
        song = self.song_with_track()
        song.root_note, song.scale_name, song.scale_mode = 0, "Major", True
        song.scale_intervals = (0, 2, 4, 5, 7, 9, 11)
        surface, _ = self.make_surface(song)
        surface._bank_up(127)
        self.assertEqual(surface._base_note(), 52)
        surface._keyboard_mode(127)
        surface._scale_turn(12)
        self.assertEqual(surface._keyboard.start(song, surface._track), 48)
        surface._bank_up(127)
        surface._pad_mode(127)
        self.assertEqual(surface._base_note(), 52)
        surface._keyboard_mode(127)
        self.assertEqual(surface._keyboard.start(song, surface._track), 48)
        surface._reset_range(127)
        self.assertEqual(surface._keyboard.start(song, surface._track), 36)
        self.assertEqual(surface._base_note(), 52)

    def test_keyboard_controls_follow_live_and_preserve_drum_bank(self):
        song = self.song_with_track()
        song.root_note, song.scale_name, song.scale_mode = 0, "Major", True
        song.scale_intervals = (0, 2, 4, 5, 7, 9, 11)
        surface, connection = self.make_surface(song)
        surface._keyboard_mode(127)
        self.assertTrue(surface._keyboard.enabled)
        self.assertEqual(surface._keyboard.notes(song, surface._track)[:4], (36, 38, 40, 41))
        surface._scale_turn(1)
        self.assertEqual(surface._keyboard.start(song, surface._track), 37)
        self.assertEqual(song.root_note, 1)
        self.assertEqual(surface._base_note(), 36)
        with patch.object(surface._keyboard, "turn") as turn:
            surface._keyboard_scale_turn(1)
            turn.assert_called_with(song, 1)
            surface._keyboard_scale_turn(127)
            turn.assert_called_with(song, -1)
        surface._scale_toggle(0)
        self.assertTrue(song.scale_mode)
        surface._scale_toggle(127)
        self.assertFalse(song.scale_mode)
        self.assertEqual(surface._keyboard.notes(song, surface._track)[:4], (37, 38, 39, 40))
        self.assertEqual((song.root_note, song.scale_name, surface._keyboard.start(song, surface._track)), (1, "Major", 37))
        surface._scale_toggle(127)
        self.assertTrue(song.scale_mode)
        song.scale_mode = False
        surface._layout_changed()
        surface.update_display()
        self.assertEqual(surface._keyboard.notes(song, surface._track)[:4], (37, 38, 39, 40))
        surface._pad_mode(127)
        self.assertFalse(surface._keyboard.enabled)
        surface._scale_toggle(127)
        self.assertFalse(song.scale_mode)
        surface._bank_up(127)
        self.assertEqual(surface._base_note(), 52)

    def test_keyboard_scale_mode_set_is_idempotent_and_preserves_scale(self):
        song = self.song_with_track()
        song.root_note, song.scale_name, song.scale_mode = 0, "Major", True
        song.scale_intervals = (0, 2, 4, 5, 7, 9, 11)
        surface, connection = self.make_surface(song)
        surface._keyboard_mode(127)
        for value in (0, 0, 0):
            surface._scale_set(value)
            self.assertFalse(song.scale_mode)
        self.assertEqual(surface._keyboard.notes(song, surface._track)[:4], (36, 37, 38, 39))
        for value in (127, 127):
            surface._scale_set(value)
            self.assertTrue(song.scale_mode)
        self.assertEqual(surface._keyboard.notes(song, surface._track)[:4], (36, 38, 40, 41))
        self.assertEqual((song.root_note, song.scale_name), (0, "Major"))
        surface._pad_mode(127)
        surface._scale_set(0)
        self.assertTrue(song.scale_mode)

    def test_mute_solo_exclusive_returns_and_master(self):
        song = self.song_with_track()
        first = song.view.selected_track
        second = LiveNode(mute=False, solo=True)
        ret = LiveNode(mute=False, solo=True)
        song.tracks.append(second)
        song.return_tracks = [ret]
        song.exclusive_solo = True
        surface, connection = self.make_surface(song)
        surface._mute(127)
        surface._mute(0)
        self.assertTrue(first.mute)
        surface._solo(127)
        self.assertTrue(first.solo)
        self.assertFalse(second.solo)
        self.assertFalse(ret.solo)
        surface._solo(0)
        self.assertTrue(first.solo)
        song.exclusive_solo = False
        second.solo = True
        surface._solo(127)
        surface._solo(127)
        self.assertTrue(first.solo and second.solo)
        song.view.selected_track = ret
        surface._mute(127)
        self.assertFalse(ret.mute)
        song.view.selected_track = LiveNode()
        surface._mute(127)
        surface._solo(127)
        self.assertIsNone(surface._mix_track())

    def test_note_selection_per_track_and_rack_mouse_selection(self):
        song = self.song_with_track()
        first = song.view.selected_track
        surface, connection = self.make_surface(song)
        surface._select_note(0)
        self.assertEqual(surface._selected_note(), 0)
        second = LiveNode(color=0, mute=False, solo=False)
        song.tracks.append(second)
        song.view.selected_track = second
        surface._layout_changed()
        surface._select_note(40)
        song.view.selected_track = first
        surface._layout_changed()
        surface.update_display()
        self.assertEqual(surface._selected_note(), 0)
        pads = [LiveNode(note=n, chains=[]) for n in range(128)]
        rack = LiveNode(can_have_drum_pads=True, drum_pads=pads, view=LiveNode(drum_pads_scroll_position=9, selected_drum_pad=None))
        first.devices = [rack]
        surface._layout_changed()
        surface._select_note(38)
        self.assertIs(rack.view.selected_drum_pad, pads[38])
        rack.view.selected_drum_pad = pads[42]
        self.assertEqual(surface._selected_note(), 42)
        first.devices = []
        surface._layout_changed()
        surface.update_display()
        self.assertEqual(surface._selected_note(), 0)

    def test_follow_toggle_and_external_state(self):
        song = self.song_with_track()
        surface, connection = self.make_surface(song)
        surface._follow(127)
        surface._follow(0)
        self.assertTrue(song.view.follow_song)
        surface.update_display()
        self.assertTrue(connection.sendto.call_args.args[0].endswith(b" 0 1 -1 0 0 1 ready"))
        song.view.follow_song = False
        for callback in song.view.callbacks["follow_song"]:
            callback()
        surface.update_display()
        self.assertTrue(connection.sendto.call_args.args[0].endswith(b" 0 0 -1 0 0 1 ready"))

    def test_tap_and_metronome(self):
        song = self.song_with_track()
        song.tap_tempo = Mock()
        surface, connection = self.make_surface(song)
        surface._tap(0)
        song.tap_tempo.assert_not_called()
        surface._tap(127)
        song.tap_tempo.assert_called_once_with()
        surface._metronome(127)
        self.assertTrue(song.metronome)
        surface._metronome(0)
        self.assertTrue(song.metronome)
        surface.update_display()
        self.assertTrue(connection.sendto.call_args.args[0].endswith(b" 1 0 -1 0 0 1 ready"))
        song.metronome = False
        surface._state_changed()
        surface.update_display()
        self.assertTrue(connection.sendto.call_args.args[0].endswith(b" 0 0 -1 0 0 1 ready"))

    def test_deleted_track_does_not_stop_heartbeat(self):
        song = self.song_with_track()
        first = song.view.selected_track
        surface, connection = self.make_surface(song)
        with patch.object(surface_module.time, "monotonic", return_value=10):
            surface.update_display()
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 0 11 36 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        first.deleted = True
        song.view.selected_track = LiveNode(color=0xFF0000)
        song.tracks = [song.view.selected_track]
        surface._layout_changed()
        with patch.object(surface_module.time, "monotonic", return_value=11):
            surface.update_display()
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 0 1 36 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        self.assertIsNone(surface._last_error)
        self.assertFalse(surface._layout_dirty)

    def test_cleanup_continues_after_invalid_handle(self):
        surface, _ = self.make_surface(LiveNode())
        bad, good = LiveNode(), LiveNode()
        callback = lambda: None
        bad.callbacks["color"] = [callback]
        good.callbacks["color"] = [callback]
        bad.deleted = True
        surface._listeners = [(bad, "color", callback), (good, "color", callback)]
        surface._remove_listeners()
        self.assertEqual(surface._listeners, [])
        self.assertEqual(good.callbacks["color"], [])
        surface._remove_listeners()

    def test_device_overlay_restores_volume_without_changing_playing_modes(self):
        song = self.song_with_track()
        track = song.view.selected_track
        device = LiveNode(name="Filter", parameters=[])
        track.devices = [device]
        track.view = LiveNode(selected_device=device)
        song.view.select_device = Mock(side_effect=lambda d: setattr(track.view, "selected_device", d))
        surface, connection = self.make_surface(song)
        surface._application.view.show_view = Mock()
        surface._mixer.enabled = True
        surface._keyboard.enabled = True
        surface._device_action("toggle")
        self.assertTrue(surface._devices.enabled)
        self.assertTrue(surface._keyboard.enabled)
        self.assertFalse(surface._mixer.enabled)
        self.assertTrue(any(call.args[0].startswith(b"MDEV1 ") for call in connection.sendto.call_args_list))
        surface._device_action("toggle")
        self.assertFalse(surface._devices.enabled)
        self.assertTrue(surface._mixer.enabled)
        surface._device_action("toggle")
        surface._pad_mode(127)
        self.assertFalse(surface._devices.enabled)
        surface.disconnect()

    def test_clear_pad_routes_only_to_events_selection(self):
        surface, connection = self.make_surface(self.song_with_track())
        surface._selection.enabled = True
        surface._selection.can_erase = Mock(return_value=True)
        surface._selection.erase = Mock()
        surface._editor.execute = Mock(return_value=False)
        surface._edit(9)
        surface._selection.erase.assert_called_once_with()
        surface._editor.execute.assert_not_called()
        surface._selection.enabled = False
        surface._edit(9)
        self.assertEqual(surface._selection.erase.call_count, 1)
        surface._editor.execute.assert_called_once_with(9)

    def test_rejected_clip_edit_keeps_pattern_mode(self):
        surface, connection = self.make_surface(self.song_with_track())
        surface._pattern.enabled = True
        surface._pattern.duplicate = Mock(side_effect=RuntimeError('clip cannot be duplicated'))
        surface._pattern_action('duplicate')
        self.assertTrue(surface._pattern.enabled)
        self.assertTrue(any('clip cannot be duplicated' in message for message in surface.messages))

    def test_temporary_api_error_retries_without_log_flood(self):
        surface, connection = self.make_surface(self.song_with_track())
        surface._rewire = Mock(side_effect=[TypeError("invalid handle"), TypeError("invalid handle"), None])
        for now in [10, 10.1, 11.1]:
            with patch.object(surface_module.time, "monotonic", return_value=now):
                surface.update_display()
        self.assertEqual(surface._rewire.call_count, 2)
        self.assertEqual(sum("state unavailable" in m for m in surface.messages), 1)
        connection.sendto.assert_not_called()
        with patch.object(surface_module.time, "monotonic", return_value=12.2):
            surface.update_display()
        self.assertIsNone(surface._last_error)
        self.assertTrue(any("state recovered" in m for m in surface.messages))
        packets = [call.args[0] for call in connection.sendto.call_args_list]
        self.assertEqual(sum(packet.startswith(b'MO1 ') for packet in packets), 1)
        legacy = [packet for packet in packets if not packet.startswith(b'MO1 ')]
        self.assertEqual(len(legacy), 6)
        self.assertTrue(legacy[0].startswith(b'MR2 '))

    def test_track_color_change_without_instrument_inspection(self):
        song = self.song_with_track(0xFF0000)
        first = song.view.selected_track
        surface, connection = self.make_surface(song)
        with patch.object(surface_module.time, "monotonic", return_value=10):
            surface.update_display()
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 0 1 36 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        first.color = 0x00FF00
        first.callbacks["color"][0]()
        with patch.object(surface_module.time, "monotonic", return_value=10.1):
            surface.update_display()
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 0 7 36 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        second = LiveNode(color=0x0000FF)
        song.tracks.append(second)
        song.view.selected_track = second
        song.view.callbacks["selected_track"][0]()
        with patch.object(surface_module.time, "monotonic", return_value=10.2):
            surface.update_display()
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 0 11 36 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        self.assertEqual(first.callbacks["color"], [])
        self.assertFalse(first.arm)
        self.assertFalse(first.implicit_arm)

    def test_rec_opens_actual_recording_clip_after_quantized_start(self):
        song = self.song_with_track()
        track = song.view.selected_track
        track.arm, track.playing_slot_index = True, -1
        slot = LiveNode(has_clip=False, clip=None)
        track.clip_slots = [slot]
        song.view.highlighted_clip_slot = slot
        song.view.detail_clip = None
        surface, _ = self.make_surface(song)
        surface.application().view.show_view = Mock()
        surface._record(127)
        self.assertTrue(song.session_record)
        surface.update_display()
        self.assertIsNone(song.view.detail_clip)
        clip = LiveNode(is_recording=True, is_playing=True)
        slot.clip, slot.has_clip = clip, True
        track.playing_slot_index = 0
        surface.update_display()
        self.assertIs(song.view.detail_clip, clip)
        surface.application().view.show_view.assert_called_once_with("Detail/Clip")
        self.assertTrue(track.arm)
        # Once shown, mouse selection is no longer overridden.
        song.view.detail_clip = object()
        surface.update_display()
        self.assertIsNot(song.view.detail_clip, clip)

    def test_rec_focus_follows_playing_slot_and_cancels_on_track_change(self):
        song = self.song_with_track()
        track = song.view.selected_track
        track.arm, track.playing_slot_index = True, 1
        actual = LiveNode(has_clip=True, clip=LiveNode(is_playing=True))
        selected = LiveNode(has_clip=False, clip=None)
        track.clip_slots = [selected, actual]
        song.view.highlighted_clip_slot = selected
        surface, _ = self.make_surface(song)
        surface.application().view.show_view = Mock()
        surface._record(127)
        surface.update_display()
        self.assertIs(song.view.detail_clip, actual.clip)
        self.assertIs(song.view.highlighted_clip_slot, actual)
        surface._record(127)
        track.playing_slot_index = -1
        surface._record(127)
        song.view.selected_track = LiveNode()
        surface._follow_recording_clip()
        self.assertIsNone(surface._record_focus)

    def test_grid_overlay_restores_step_and_closes_on_other_encoder_mode(self):
        song = self.song_with_track()
        track = song.view.selected_track
        clip = LiveNode(is_midi_clip=True, canonical_parent=track, looping=True, loop_start=0.0, loop_end=4.0,
                        get_all_notes_extended=Mock(return_value=[]))
        song.view.detail_clip = clip
        surface, connection = self.make_surface(song)
        surface.application().view.is_view_visible = lambda name: name == "Detail/Clip"
        surface._step_action("toggle")
        surface._grid_action("grid_hold", True)
        self.assertTrue(surface._sequencer.enabled)
        self.assertTrue(surface._sequencer.grid_enabled)
        surface._grid_action("grid_turn", 1)
        self.assertAlmostEqual(surface._sequencer.STEP, 1/6)
        self.assertTrue(any(b" 1 1 1/16T ready" in c.args[0] for c in connection.sendto.call_args_list))
        surface._grid_action("grid_hold", False)
        self.assertFalse(surface._sequencer.grid_enabled)
        self.assertTrue(surface._sequencer.enabled)
        surface._grid_action("grid_hold", True)
        surface._mixer_action("toggle_tempo")
        self.assertFalse(surface._sequencer.grid_enabled)

    def test_held_grid_preserves_encoder_overlays_and_selected_resolution(self):
        song = self.song_with_track()
        track = song.view.selected_track
        song.view.detail_clip = LiveNode(is_midi_clip=True, canonical_parent=track, looping=True,
                                        loop_start=0.0, loop_end=4.0, get_all_notes_extended=Mock(return_value=[]))
        surface, _ = self.make_surface(song)
        surface.application().view.is_view_visible = lambda name: name == "Detail/Clip"
        for overlay in ("volume", "tempo", "browser"):
            with self.subTest(overlay=overlay):
                surface._mixer.enabled = overlay == "volume"
                surface._mixer.tempo = overlay == "tempo"
                surface._browser.enabled = overlay == "browser"
                before = (surface._mixer.enabled, surface._mixer.tempo, surface._browser.enabled)
                surface._grid_action("grid_hold", True)
                surface._grid_action("grid_hold", True)
                self.assertTrue(surface._sequencer.grid_enabled)
                surface._grid_action("grid_turn", 1)
                selected = surface._sequencer.grid_index
                surface._grid_action("grid_hold", False)
                self.assertFalse(surface._sequencer.grid_enabled)
                self.assertEqual(surface._sequencer.grid_index, selected)
                self.assertEqual((surface._mixer.enabled, surface._mixer.tempo, surface._browser.enabled), before)

    def test_return_and_main_disable_notes_and_mute_but_keep_volume_navigation(self):
        song = self.song_with_track()
        ret, main = LiveNode(mute=False, solo=False), LiveNode()
        song.return_tracks, song.master_track = [ret], main
        surface, connection = self.make_surface(song)
        for track in (ret, main):
            song.view.selected_track = track
            surface._layout_changed()
            surface.update_display()
            expected = "MN1 {} 0 0 ready".format(surface._feedback_epoch).encode("ascii")
            self.assertTrue(any(c.args[0] == expected for c in connection.sendto.call_args_list))
            surface._mute(127)
        self.assertFalse(ret.mute)
        self.assertEqual(surface._mixer.tracks()[-2:], [ret, main])

    def test_rec_preserves_all_manual_and_implicit_arm_states(self):
        song = self.song_with_track()
        other = LiveNode(arm=True, implicit_arm=True)
        song.tracks.append(other)
        surface, _ = self.make_surface(song)
        surface._record(127)
        self.assertTrue(song.session_record)
        self.assertFalse(song.tracks[0].arm)
        self.assertFalse(song.tracks[0].implicit_arm)
        self.assertTrue(other.arm)
        self.assertTrue(other.implicit_arm)
        surface._record(127)
        self.assertFalse(song.session_record)
        self.assertTrue(other.arm)
        self.assertTrue(other.implicit_arm)

    def test_rec_works_without_any_instrument_or_selected_track_lookup(self):
        song = LiveNode(session_record=False, session_record_status=0)
        surface, _ = self.make_surface(song)
        surface._record(127)
        self.assertTrue(song.session_record)
        surface._record(0)
        self.assertTrue(song.session_record)

    def test_rec_cancels_active_or_pending_session_recording(self):
        for status in [1, 2]:
            song = LiveNode(session_record=False, session_record_status=status)
            surface, _ = self.make_surface(song)
            surface._record(127)
            self.assertFalse(song.session_record)

    def test_record_led_follows_session_status(self):
        song = self.song_with_track()
        song.session_record_status = 2
        surface, connection = self.make_surface(song)
        with patch.object(surface_module.time, "monotonic", return_value=10):
            surface.update_display()
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 1 11 36 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))

    def test_arrangement_rec_preserves_position_playback_and_arm(self):
        for playing in [False, True]:
            song = self.song_with_track()
            song.is_playing = playing
            song.current_song_time = 42.5
            song.start_time = 32.0
            song.start_playing = Mock(side_effect=AssertionError("REC must not restart playback"))
            song.session_record = True
            surface, _ = self.make_surface(song)
            surface.application().view.focused_document_view = "Arranger"
            surface._record(127)
            self.assertTrue(song.record_mode)
            surface._record(0)
            self.assertTrue(song.record_mode)
            surface._record(127)
            self.assertFalse(song.record_mode)
            self.assertTrue(song.session_record)
            self.assertEqual(song.current_song_time, 42.5)
            self.assertEqual(song.start_time, 32.0)
            self.assertEqual(song.is_playing, playing)
            self.assertFalse(song.tracks[0].arm)
            self.assertFalse(song.tracks[0].implicit_arm)
            song.start_playing.assert_not_called()

    def test_rec_uses_focused_view_at_press_even_before_display_update(self):
        song = self.song_with_track()
        surface, _ = self.make_surface(song)
        surface._record(127)
        self.assertTrue(song.session_record)
        surface.application().view.focused_document_view = "Arranger"
        surface._record(127)
        self.assertTrue(song.record_mode)
        self.assertTrue(song.session_record)
        surface.application().view.focused_document_view = "Session"
        surface._record(127)
        self.assertFalse(song.session_record)
        self.assertTrue(song.record_mode)

    def test_record_led_follows_focused_view_and_native_record_changes(self):
        song = self.song_with_track()
        song.session_record_status = 2
        surface, connection = self.make_surface(song)
        with patch.object(surface_module.time, "monotonic", return_value=10):
            surface.update_display()
            self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 1 11 36 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
            view = surface.application().view
            view.focused_document_view = "Arranger"
            view.callbacks["focused_document_view"][0]()
            surface.update_display()
            self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 0 11 36 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
            song.record_mode = True
            song.callbacks["record_mode"][0]()
            surface.update_display()
            self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 1 11 36 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        surface.disconnect()
        self.assertEqual(view.callbacks["focused_document_view"], [])

    def test_rack_banking_follows_live_scroll_and_track_switches(self):
        song = self.song_with_track()
        rack = LiveNode(can_have_drum_pads=True, view=LiveNode(drum_pads_scroll_position=9))
        first = song.view.selected_track
        first.devices = [rack]
        surface, connection = self.make_surface(song)
        surface._bank_up(127)
        self.assertEqual(rack.view.drum_pads_scroll_position, 13)
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 0 11 52 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        surface._bank_up(0)
        self.assertEqual(rack.view.drum_pads_scroll_position, 13)
        rack.view.drum_pads_scroll_position = 2
        rack.view.callbacks["drum_pads_scroll_position"][0]()
        surface.update_display()
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 0 11 8 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        second_rack = LiveNode(can_have_drum_pads=True, view=LiveNode(drum_pads_scroll_position=20))
        second = LiveNode(color=0xFF0000, devices=[second_rack])
        song.tracks.append(second)
        song.view.selected_track = second
        surface._layout_changed()
        surface.update_display()
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 0 1 80 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        self.assertEqual(rack.view.callbacks["drum_pads_scroll_position"], [])

    def test_bank_limits_and_generic_track_memory(self):
        song = self.song_with_track()
        first = song.view.selected_track
        surface, _ = self.make_surface(song)
        for _ in range(20):
            surface._bank_down(127)
        self.assertEqual(surface._base_note(), 0)
        for _ in range(20):
            surface._bank_up(127)
        self.assertEqual(surface._base_note(), 112)
        second = LiveNode(color=0xFF0000)
        song.tracks.append(second)
        song.view.selected_track = second
        surface._layout_changed()
        surface._bank_up(127)
        self.assertEqual(surface._base_note(), 52)
        song.view.selected_track = first
        surface._layout_changed()
        surface.update_display()
        self.assertEqual(surface._base_note(), 112)
        song.tracks.remove(second)
        surface._layout_changed()
        surface.update_display()
        self.assertNotIn(second, surface._fallback_bases)

    def test_selected_rack_and_removing_rack_keep_heartbeat_alive(self):
        song = self.song_with_track()
        rack_a = LiveNode(can_have_drum_pads=True, view=LiveNode(drum_pads_scroll_position=0))
        rack_b = LiveNode(can_have_drum_pads=True, view=LiveNode(drum_pads_scroll_position=28))
        track = song.view.selected_track
        track.devices = [rack_a, rack_b]
        track.view = LiveNode(selected_device=rack_b)
        surface, connection = self.make_surface(song)
        surface._bank_up(127)
        self.assertEqual(surface._base_note(), 112)
        self.assertEqual(rack_a.view.drum_pads_scroll_position, 0)
        surface._bank_down(127)
        self.assertEqual(rack_b.view.drum_pads_scroll_position, 25)
        rack_b.view.deleted = True
        track.devices = []
        track.view.selected_device = None
        track.callbacks["devices"][0]()
        surface.update_display()
        self.assertEqual(surface._base_note(), 36)
        self.assertIsNone(surface._last_error)
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 0 11 36 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))

    def test_drum_rack_inside_instrument_rack_scrolls_visible_grid(self):
        song = self.song_with_track()
        drum = LiveNode(name="Kit", can_have_drum_pads=True,
                        view=LiveNode(drum_pads_scroll_position=9))
        chain = LiveNode(devices=[drum])
        wrapper = LiveNode(can_have_drum_pads=False, can_have_chains=True,
                           chains=[chain], view=LiveNode(selected_chain=chain))
        track = song.view.selected_track
        track.devices = [wrapper]
        track.view = LiveNode(selected_device=wrapper)
        surface, connection = self.make_surface(song)
        surface._bank_up(127)
        self.assertIs(surface._rack, drum)
        self.assertEqual(drum.view.drum_pads_scroll_position, 13)
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 65535 0 0 11 52 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        drum.view.drum_pads_scroll_position = 5
        drum.view.callbacks["drum_pads_scroll_position"][0]()
        surface.update_display()
        self.assertEqual(surface._base_note(), 20)
        replacement = LiveNode(can_have_drum_pads=True,
                               view=LiveNode(drum_pads_scroll_position=17))
        drum.view.deleted = True
        chain.devices = [replacement]
        chain.callbacks["devices"][0]()
        surface.update_display()
        self.assertIs(surface._rack, replacement)
        self.assertEqual(surface._base_note(), 68)
        self.assertIsNone(surface._last_error)

    def test_nested_wrapper_selection_and_chain_changes_choose_visible_kit(self):
        song = self.song_with_track()
        drums = [LiveNode(can_have_drum_pads=True,
                          view=LiveNode(drum_pads_scroll_position=row)) for row in [9, 13]]
        chains = [LiveNode(devices=[drum]) for drum in drums]
        wrapper = LiveNode(can_have_chains=True, chains=chains,
                           view=LiveNode(selected_chain=chains[1]))
        outer_chain = LiveNode(devices=[wrapper])
        outer = LiveNode(can_have_chains=True, chains=[outer_chain],
                         view=LiveNode(selected_chain=outer_chain))
        song.view.selected_track.devices = [outer]
        surface, _ = self.make_surface(song)
        surface.update_display()
        self.assertIs(surface._rack, drums[1])
        wrapper.view.selected_chain = chains[0]
        wrapper.view.callbacks["selected_chain"][0]()
        surface.update_display()
        self.assertIs(surface._rack, drums[0])
        chains[0].devices = []
        wrapper.chains = [chains[0]]
        wrapper.callbacks["chains"][0]()
        surface.update_display()
        self.assertIsNone(surface._rack)
        self.assertEqual(surface._base_note(), 36)

    def test_occupied_pads_follow_chain_edits_and_visible_bank(self):
        song = self.song_with_track()
        pads = [LiveNode(note=note, chains=[]) for note in range(128)]
        for note in [36, 40, 51, 52]:
            pads[note].chains = [LiveNode()]
        rack = LiveNode(can_have_drum_pads=True, drum_pads=pads,
                        view=LiveNode(drum_pads_scroll_position=9))
        song.view.selected_track.devices = [rack]
        surface, connection = self.make_surface(song)
        surface.update_display()
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 32785 0 0 11 36 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        pads[40].chains = []
        pads[40].callbacks["chains"][0]()
        surface.update_display()
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 32769 0 0 11 36 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        surface._bank_up(127)
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 1 0 0 11 52 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        pads[52].chains = []
        pads[52].callbacks["chains"][0]()
        surface.update_display()
        self.assertEqual(connection.sendto.call_args.args[0], "MC7 0 0 0 11 52 {} 0 0 -1 0 0 1 ready".format(surface._feedback_epoch).encode("ascii"))
        surface.disconnect()
        self.assertEqual(pads[52].callbacks["chains"], [])

    def test_state_epoch_changes_without_claiming_live_feedback_or_changing_arm(self):
        song = self.song_with_track()
        song.view.selected_track.has_midi_input = True
        song.view.selected_track.arm = True
        surface, _ = self.make_surface(song)
        surface.update_display()
        epoch = surface._feedback_epoch
        second = LiveNode(color=0xFF0000, arm=False, implicit_arm=False)
        song.tracks.append(second)
        song.view.selected_track = second
        surface._layout_changed()
        surface.update_display()
        self.assertGreater(surface._feedback_epoch, epoch)
        self.assertTrue(song.tracks[0].arm)
        self.assertFalse(second.arm)
        self.assertFalse(second.implicit_arm)
        surface.set_feedback_channels.assert_not_called()
        surface.set_controlled_track.assert_not_called()
        surface.set_pad_translations.assert_not_called()
        surface._c_instance.set_note_translation.assert_not_called()
        surface.disconnect()

    def test_nested_sampler_activity_uses_udp_and_removes_listeners_on_bank_change(self):
        song = self.song_with_track()
        sampler = LiveNode(playing_position_enabled=False, playing_position=0.0)
        inner = LiveNode(devices=[sampler])
        wrapper = LiveNode(can_have_chains=True, chains=[inner])
        chain = LiveNode(devices=[wrapper])
        pads = [LiveNode(note=note, chains=[]) for note in range(128)]
        pads[36].chains = [chain]
        rack = LiveNode(can_have_drum_pads=True, drum_pads=pads,
                        view=LiveNode(drum_pads_scroll_position=9))
        song.view.selected_track.devices = [rack]
        surface, connection = self.make_surface(song)
        surface.update_display()
        sampler.playing_position_enabled = True
        sampler.callbacks["playing_position_enabled"][0]()
        self.assertEqual(connection.sendto.call_args.args[0],
                         "MP1 {} 36".format(surface._feedback_epoch).encode("ascii"))
        surface._send_midi.assert_not_called()
        surface._bank_up(127)
        self.assertEqual(sampler.callbacks["playing_position_enabled"], [])
        self.assertEqual(sampler.callbacks["playing_position"], [])
        surface.disconnect()



if __name__ == "__main__":
    unittest.main()
