import importlib.util
import pathlib
import sys
import types
import unittest
from unittest.mock import Mock, patch

root = pathlib.Path(__file__).parents[1] / "ableton/MikroCompanion"
package = types.ModuleType("step_test")
package.__path__ = [str(root)]
sys.modules["step_test"] = package
spec = importlib.util.spec_from_file_location("step_test.sequencer", root / "sequencer.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class SequencerTests(unittest.TestCase):
    def test_dense_clip_display_reads_each_note_once(self):
        reads = []
        class Note:
            start_time = 2.01
            @property
            def pitch(self):
                reads.append(1)
                return 36
        self.notes = [Note() for _ in range(1024)]
        for index, note in enumerate(self.notes):
            note.start_time = 2.01 + (index % 16) * 0.25
        self.seq.toggle(36)
        reads.clear()
        fields = self.seq.packet(3, 1).split()
        self.assertEqual(int(fields[8]), 65535)
        self.assertEqual(len(reads), len(self.notes))
        self.clip.add_new_notes.assert_not_called()
        self.clip.remove_notes_by_id.assert_not_called()

    def test_empty_session_slot_creates_and_fires_only_on_first_step(self):
        self.track.has_midi_input = True
        self.slot = types.SimpleNamespace(canonical_parent=self.track, has_clip=False, clip=None)
        def create(length):
            self.assertEqual(length, 3.0)
            self.clip.loop_start, self.clip.loop_end = 0.0, length
            self.slot.clip, self.slot.has_clip = self.clip, True
        self.slot.create_clip, self.slot.fire = Mock(side_effect=create), Mock()
        self.song.signature_numerator, self.song.signature_denominator = 3, 4
        self.song.view.highlighted_clip_slot = self.slot
        # An old Detail clip must not override a newly selected empty slot.
        self.seq = module.Sequencer(self.song, lambda: self.visible, lambda: True)
        self.seq.toggle(38)
        self.assertTrue(self.seq.enabled)
        self.assertEqual(self.seq.bounds(), (0.0, 3.0))
        self.slot.create_clip.assert_not_called()
        self.slot.fire.assert_not_called()
        self.assertEqual(self.seq.packet(3, 1).split()[7], '4095')
        clip_api = types.ModuleType('Live.Clip')
        clip_api.MidiNoteSpecification = lambda **kw: types.SimpleNamespace(**kw)
        song_api = types.ModuleType('Live.Song')
        song_api.Quantization = types.SimpleNamespace(q_no_q=object())
        with patch.dict(sys.modules, {'Live.Clip': clip_api, 'Live.Song': song_api}):
            self.seq.pad(2)
            self.seq.pad(3)
        self.slot.create_clip.assert_called_once_with(3.0)
        self.slot.fire.assert_called_once_with(force_legato=True, launch_quantization=song_api.Quantization.q_no_q)
        self.assertEqual(self.clip.add_new_notes.call_args_list[0].args[0][0].start_time, 0.5)
        self.assertTrue(self.seq.enabled)
        self.song.view.highlighted_clip_slot = None
        self.visible = False
        self.seq.sync()
        self.assertFalse(self.seq.enabled)

    def test_empty_slot_rejects_audio_tracks_and_other_track_slots(self):
        self.track.has_midi_input = False
        slot = types.SimpleNamespace(canonical_parent=self.track, has_clip=False)
        self.song.view.detail_clip = None
        self.song.view.highlighted_clip_slot = slot
        self.seq = module.Sequencer(self.song, lambda: True, lambda: True)
        self.seq.toggle(36)
        self.assertFalse(self.seq.enabled)
        self.track.has_midi_input = True
        slot.canonical_parent = object()
        self.seq.toggle(36)
        self.assertFalse(self.seq.enabled)

    def test_track_header_uses_empty_cell_in_selected_session_scene(self):
        self.track.has_midi_input = True
        scene_a, scene_b = object(), object()
        self.song.scenes = (scene_a, scene_b)
        self.song.view.selected_scene = scene_b
        first = types.SimpleNamespace(canonical_parent=self.track, has_clip=False)
        second = types.SimpleNamespace(canonical_parent=self.track, has_clip=False,
                                       create_clip=Mock(), fire=Mock())
        self.track.clip_slots = (first, second)
        self.song.view.detail_clip = None
        # A stale highlighted cell belongs to another track.
        foreign = types.SimpleNamespace(canonical_parent=object(), has_clip=False)
        self.song.view.highlighted_clip_slot = foreign
        self.seq = module.Sequencer(self.song, lambda: False, lambda: True)
        self.seq.toggle(60)
        self.assertTrue(self.seq.enabled)
        self.assertIs(self.seq.slot, second)
        self.assertEqual(self.seq.pitch, 60)
        self.assertEqual(self.seq.packet(1, 1).split()[2:4], ["1", "1"])
        second.create_clip.assert_not_called()
        second.fire.assert_not_called()
        self.assertIs(self.song.view.highlighted_clip_slot, foreign)
        self.seq.session_visible = lambda: False
        self.seq.sync()
        self.assertFalse(self.seq.enabled)

    def test_track_header_fallback_does_not_override_an_open_clip(self):
        self.track.has_midi_input = True
        scene = object()
        self.song.scenes = (scene,)
        self.song.view.selected_scene = scene
        self.track.clip_slots = (types.SimpleNamespace(canonical_parent=self.track, has_clip=False),)
        self.seq = module.Sequencer(self.song, lambda: True, lambda: True)
        self.seq.toggle(60)
        self.assertIs(self.seq.clip, self.clip)
        self.assertIsNone(self.seq.slot)
        self.assertTrue(self.seq.enabled)

    def setUp(self):
        self.track = types.SimpleNamespace()
        self.notes = [types.SimpleNamespace(note_id=1, pitch=36, start_time=2.01),
                      types.SimpleNamespace(note_id=2, pitch=38, start_time=2.01),
                      types.SimpleNamespace(note_id=3, pitch=36, start_time=6.0)]
        self.clip = types.SimpleNamespace(is_midi_clip=True, canonical_parent=self.track,
            looping=True, loop_start=2.0, loop_end=10.0, start_marker=0.0, end_marker=10.0,
            color=0xFF0000, is_playing=True, playing_position=6.25)
        self.clip.get_all_notes_extended = Mock(side_effect=lambda: list(self.notes))
        self.clip.remove_notes_by_id = Mock(side_effect=lambda ids: self.notes.__setitem__(slice(None), [n for n in self.notes if n.note_id not in ids]))
        self.clip.add_new_notes = Mock()
        self.clip.duplicate_loop = Mock(side_effect=lambda: setattr(self.clip, "loop_end", 18.0))
        self.song = types.SimpleNamespace(is_playing=True, view=types.SimpleNamespace(selected_track=self.track, detail_clip=self.clip), begin_undo_step=Mock(), end_undo_step=Mock())
        self.visible = True
        self.seq = module.Sequencer(self.song, lambda: self.visible)

    def test_banks_strip_and_playhead_use_loop_bounds(self):
        self.seq.toggle(36)
        self.assertTrue(self.seq.enabled)
        self.assertEqual(self.seq.pages(), 2)
        fields = self.seq.packet(3, 1).split()
        self.assertEqual(fields[:10], ["MS1", "3", "1", "1", "36", "0", "2", "65535", "1", "-1"])
        self.assertEqual(fields[11:14], ["0", "13", "13"])
        self.seq.navigate(1)
        fields = self.seq.packet(3, 2).split()
        self.assertEqual(fields[8:10], ["1", "1"])
        self.assertEqual(fields[11:13], ["12", "25"])
        self.seq.navigate(100)
        self.assertEqual(self.seq.page, 1)

    def test_toggle_step_removes_only_its_pitch_and_adds_native_note(self):
        self.seq.toggle(36)
        self.seq.pad(0)
        self.clip.remove_notes_by_id.assert_called_once_with((1,))
        self.assertEqual([n.note_id for n in self.notes], [2, 3])
        live = types.ModuleType("Live")
        clip_api = types.ModuleType("Live.Clip")
        clip_api.MidiNoteSpecification = lambda **kw: types.SimpleNamespace(**kw)
        with patch.dict(sys.modules, {"Live": live, "Live.Clip": clip_api}):
            self.seq.pad(2)
        note = self.clip.add_new_notes.call_args.args[0][0]
        self.assertEqual((note.pitch, note.start_time, note.duration, note.velocity), (36, 2.5, 0.25, 100))
        self.assertEqual(self.song.begin_undo_step.call_count, 2)
        self.assertEqual(self.song.end_undo_step.call_count, 2)

    def test_performed_notes_use_nearest_step_without_quantizing_or_duplicating(self):
        times = (2.99, 3.01, 2.875, 2.874, 3.126)
        self.notes = [types.SimpleNamespace(note_id=i+10, pitch=36, start_time=t, velocity=40+i*10)
                      for i, t in enumerate(times)]
        self.notes.append(types.SimpleNamespace(note_id=99, pitch=38, start_time=3.01))
        self.seq.toggle(36)
        self.assertEqual([n.start_time for n in self.notes[:5]], list(times))
        self.assertEqual([n.velocity for n in self.notes[:5]], [40, 50, 60, 70, 80])
        self.assertEqual([n.note_id for n in self.seq.hits(4)], [10, 11, 12])
        self.assertEqual([n.note_id for n in self.seq.hits(3)], [13])
        self.assertEqual([n.note_id for n in self.seq.hits(5)], [14])
        self.assertEqual(int(self.seq.packet(3, 1).split()[8]), (1<<3)|(1<<4)|(1<<5))
        self.seq.pad(4)
        self.clip.remove_notes_by_id.assert_called_once_with((10, 11, 12))
        self.clip.add_new_notes.assert_not_called()
        self.assertEqual([n.note_id for n in self.notes], [13, 14, 99])
        self.song.begin_undo_step.assert_called_once_with()
        self.song.end_undo_step.assert_called_once_with()

    def test_early_note_at_page_boundary_belongs_to_next_page(self):
        self.notes = [types.SimpleNamespace(note_id=10, pitch=36, start_time=5.99)]
        self.seq.toggle(36)
        self.assertEqual(self.seq.hits(15), [])
        self.seq.navigate(1)
        self.assertEqual([n.note_id for n in self.seq.hits(0)], [10])
        self.seq.pad(0)
        self.clip.remove_notes_by_id.assert_called_once_with((10,))
        self.clip.add_new_notes.assert_not_called()

    def test_loop_end_anticipation_maps_to_first_step_but_outside_notes_are_ignored(self):
        self.notes = [types.SimpleNamespace(note_id=i, pitch=36, start_time=t)
                      for i, t in enumerate((1.99, 2.01, 9.99, 10.0))]
        self.seq.toggle(36)
        self.assertEqual([n.note_id for n in self.seq.hits(0)], [1, 2])
        self.seq.navigate(1)
        self.assertEqual(self.seq.hits(15), [])
        self.seq.navigate(-1)
        self.seq.pad(0)
        self.assertEqual([n.note_id for n in self.notes], [0, 3])

    def test_unlooped_partial_last_step_keeps_late_notes(self):
        self.clip.looping = False
        self.clip.start_marker, self.clip.end_marker = 2.0, 2.6
        self.notes = [types.SimpleNamespace(note_id=10, pitch=36, start_time=2.59)]
        self.seq.toggle(36)
        self.assertEqual([n.note_id for n in self.seq.hits(2)], [10])
        self.assertEqual(self.seq.hits(0), [])
        self.seq.pad(2)
        self.clip.remove_notes_by_id.assert_called_once_with((10,))

    def test_triplet_grid_assigns_early_notes_once(self):
        self.seq.STEP = 1.0 / 6
        self.notes = [types.SimpleNamespace(note_id=i, pitch=36, start_time=t)
                      for i, t in enumerate((2.32, 2.34, 2.25))]
        self.seq.toggle(36)
        groups = [[n.note_id for n in self.seq.hits(step)] for step in range(16)]
        self.assertEqual(groups[2], [0, 1, 2])
        self.assertEqual(sum(len(group) for group in groups), 3)

    def test_duplicate_and_partial_page_and_target_switch(self):
        self.seq.toggle(36)
        self.seq.duplicate()
        self.assertEqual(self.seq.pages(), 4)
        self.clip.loop_end = 6.1
        self.seq.navigate(1)
        self.assertEqual(int(self.seq.packet(3, 2).split()[7]), 1)
        self.seq.pad(1)
        self.clip.add_new_notes.assert_not_called()
        self.song.view.selected_track = types.SimpleNamespace(name="Other")
        self.seq.sync(3)
        self.assertFalse(self.seq.enabled)
        self.assertIsNone(self.seq.clip)

    def test_pitch_selection_keeps_page_and_edits_new_sound(self):
        self.seq.toggle(36)
        self.seq.navigate(1)
        self.seq.select_pitch(38)
        self.assertTrue(self.seq.enabled)
        self.assertEqual((self.seq.pitch, self.seq.page), (38, 1))
        self.seq.select_pitch(200)
        self.assertEqual(self.seq.pitch, 38)
        live = types.ModuleType("Live")
        clip_api = types.ModuleType("Live.Clip")
        clip_api.MidiNoteSpecification = lambda **kw: types.SimpleNamespace(**kw)
        with patch.dict(sys.modules, {"Live": live, "Live.Clip": clip_api}):
            self.seq.pad(0)
        note = self.clip.add_new_notes.call_args.args[0][0]
        self.assertEqual((note.pitch, note.start_time), (38, 6.0))

    def grid_api(self):
        api = types.ModuleType("Live.Clip")
        api.GridQuantization = types.SimpleNamespace(g_quarter=1, g_eighth=2, g_sixteenth=3, g_thirtysecond=4)
        live = types.ModuleType("Live")
        live.Clip = api
        return patch.dict(sys.modules, {"Live": live, "Live.Clip": api})

    def test_grid_changes_step_resolution_and_clip_grid_without_moving_notes(self):
        self.clip.view = types.SimpleNamespace(grid_quantization=3, grid_is_triplet=False)
        with self.grid_api():
            self.seq.toggle(36)
            self.seq.navigate(1)
            self.seq.grid_toggle()
            self.seq.grid_turn(1)
            self.assertAlmostEqual(self.seq.STEP, 1/6)
            self.assertEqual(self.clip.view.grid_quantization, 3)
            self.assertTrue(self.clip.view.grid_is_triplet)
            self.assertEqual(self.seq.page, 1)
            self.assertIn(" 1 1 1/16T ready", self.seq.grid_packet(3))
            self.seq.grid_turn(100)
            self.assertEqual(self.seq.grid_index, 7)
            self.seq.grid_turn(-100)
            self.assertEqual(self.seq.STEP, 1.0)
            self.assertEqual(self.seq.page, 0)
        self.clip.add_new_notes.assert_not_called()
        self.clip.remove_notes_by_id.assert_not_called()
        self.assertEqual([n.start_time for n in self.notes], [2.01, 2.01, 6.0])

    def test_external_grid_change_updates_pages_and_triplet_cursor(self):
        self.clip.view = types.SimpleNamespace(grid_quantization=3, grid_is_triplet=False)
        with self.grid_api():
            self.seq.toggle(36)
            self.clip.view.grid_quantization = 4
            self.clip.view.grid_is_triplet = True
            self.clip.playing_position = 2.25
            fields = self.seq.packet(3, 1).split()
            self.assertAlmostEqual(self.seq.STEP, 1/12)
            self.assertEqual(self.seq.pages(), 6)
            self.assertEqual(fields[9], "3")
            self.assertIn("1/32T", self.seq.grid_packet(3))

    def test_empty_slot_keeps_chosen_grid_when_first_step_creates_clip(self):
        self.track.has_midi_input = True
        self.song.signature_numerator, self.song.signature_denominator = 4, 4
        self.clip.view = types.SimpleNamespace(grid_quantization=3, grid_is_triplet=False)
        slot = types.SimpleNamespace(canonical_parent=self.track, has_clip=False, clip=None, fire=Mock())
        def create(length):
            self.clip.loop_start, self.clip.loop_end = 0.0, length
            slot.clip, slot.has_clip = self.clip, True
        slot.create_clip = Mock(side_effect=create)
        self.song.view.highlighted_clip_slot = slot
        self.seq = module.Sequencer(self.song, lambda: True, lambda: True)
        with self.grid_api():
            api = sys.modules["Live.Clip"]
            api.MidiNoteSpecification = lambda **kw: types.SimpleNamespace(**kw)
            song_api = types.ModuleType("Live.Song")
            song_api.Quantization = types.SimpleNamespace(q_no_q=0)
            with patch.dict(sys.modules, {"Live.Song": song_api}):
                self.seq.grid_toggle()
                self.seq.grid_turn(1)
                self.seq.toggle(36)
                self.seq.pad(3)
            note = self.clip.add_new_notes.call_args.args[0][0]
            self.assertAlmostEqual(note.start_time, 0.5)
            self.assertAlmostEqual(note.duration, 1/6)
            self.assertEqual(self.clip.view.grid_quantization, 3)
            self.assertTrue(self.clip.view.grid_is_triplet)
            self.assertAlmostEqual(self.seq.STEP, 1/6)

    def test_grid_unavailable_without_clip_or_empty_midi_slot(self):
        self.visible = False
        self.seq.grid_toggle()
        self.assertFalse(self.seq.grid_enabled)
        self.assertEqual(self.seq.grid_packet(3), "MG1 3 0 0 1/16 ready")

    def test_disabled_sequencer_does_not_poll_notes_or_modify_clip(self):
        self.seq.packet(3, 2)
        self.clip.get_all_notes_extended.assert_not_called()
        self.seq.pad(0)
        self.seq.duplicate()
        self.clip.remove_notes_by_id.assert_not_called()
        self.clip.duplicate_loop.assert_not_called()
        self.visible = False
        self.seq.toggle(36)
        self.assertFalse(self.seq.enabled)
