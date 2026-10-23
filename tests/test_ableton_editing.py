import copy
import importlib.util
import pathlib
import sys
import types
import unittest
from unittest.mock import Mock, patch

path = pathlib.Path(__file__).parents[1] / "ableton/MikroCompanion/editing.py"
spec = importlib.util.spec_from_file_location("editing_test", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def note(ident, pitch, start):
    return types.SimpleNamespace(note_id=ident, pitch=pitch, start_time=start,
                                 duration=0.75, velocity=89, probability=0.6,
                                 release_velocity=35, velocity_deviation=12, mute=False)


class Clip:
    def __init__(self, track):
        self.is_midi_clip = True
        self.canonical_parent = types.SimpleNamespace(canonical_parent=track)
        self.view = types.SimpleNamespace(grid_quantization=16, grid_is_triplet=False)
        self.notes = [note(1, 36, 0.1), note(2, 40, 0.4)]
        self.selected = []
        self.apply_note_modifications = Mock(side_effect=self.apply)

    def get_selected_notes_extended(self):
        return copy.deepcopy([n for n in self.notes if n.note_id in self.selected])

    def get_all_notes_extended(self):
        return copy.deepcopy(self.notes)

    def apply(self, changes):
        by_id = {n.note_id: n for n in changes}
        self.notes = [by_id.get(n.note_id, n) for n in self.notes]


class EditTests(unittest.TestCase):
    def setUp(self):
        self.track = object()
        self.clip = Clip(self.track)
        self.song = types.SimpleNamespace(view=types.SimpleNamespace(selected_track=self.track, detail_clip=self.clip),
                                          can_undo=True, can_redo=False, undo=Mock(), redo=Mock(),
                                          begin_undo_step=Mock(), end_undo_step=Mock())
        self.editor = module.Editor(self.song, Mock())
        enum = types.SimpleNamespace(g_sixteenth=16, g_quarter=4, g_eighth=8)
        self.api = patch.dict(sys.modules, {"Live": types.SimpleNamespace(Clip=types.SimpleNamespace(GridQuantization=enum))})
        self.api.start()
        self.addCleanup(self.api.stop)

    def test_selected_only_and_attributes_survive_single_undo_step(self):
        self.clip.selected = [2]
        original = vars(self.clip.notes[1]).copy()
        self.assertTrue(self.editor.execute(14))
        self.assertEqual([n.pitch for n in self.clip.notes], [36, 41])
        for key, value in original.items():
            if key != "pitch":
                self.assertEqual(getattr(self.clip.notes[1], key), value)
        self.song.begin_undo_step.assert_called_once_with()
        self.song.end_undo_step.assert_called_once_with()

    def test_no_selection_quantizes_all_note_starts_only(self):
        self.editor.execute(5)
        self.assertEqual([n.start_time for n in self.clip.notes], [0.0, 0.5])
        self.assertEqual([n.duration for n in self.clip.notes], [0.75, 0.75])
        self.assertFalse(self.editor.execute(5))
        self.assertEqual(self.song.begin_undo_step.call_count, 1)

    def test_half_quantize_and_triplet_grid(self):
        self.clip.selected = [1]
        self.editor.execute(6)
        self.assertAlmostEqual(self.clip.notes[0].start_time, 0.05)
        self.assertEqual(self.clip.notes[1].start_time, 0.4)
        self.clip.view.grid_is_triplet = True
        self.assertAlmostEqual(module.grid_step(self.clip), 1.0 / 6.0)

    def test_nudge_preserves_relative_offsets_and_stops_at_zero(self):
        self.editor.execute(7)
        self.assertAlmostEqual(self.clip.notes[0].start_time, 0.0)
        self.assertAlmostEqual(self.clip.notes[1].start_time, 0.3)
        self.assertFalse(self.editor.execute(7))
        self.editor.execute(8)
        self.assertAlmostEqual(self.clip.notes[0].start_time, 0.125)
        self.assertAlmostEqual(self.clip.notes[1].start_time, 0.425)

    def test_pitch_bounds_and_octaves(self):
        self.editor.execute(16)
        self.assertEqual([n.pitch for n in self.clip.notes], [48, 52])
        self.editor.execute(15)
        self.assertEqual([n.pitch for n in self.clip.notes], [36, 40])
        self.clip.notes[0].pitch = 0
        self.assertFalse(self.editor.execute(13))
        self.assertEqual(self.clip.notes[1].pitch, 40)

    def test_missing_audio_and_other_track_clips_are_ignored(self):
        self.clip.is_midi_clip = False
        self.assertFalse(self.editor.execute(5))
        self.clip.is_midi_clip = True
        self.song.view.selected_track = object()
        self.assertFalse(self.editor.execute(14))
        self.song.view.detail_clip = None
        self.assertFalse(self.editor.execute(14))
        self.song.begin_undo_step.assert_not_called()

    def test_undo_redo_unmapped_and_grid_off_availability(self):
        self.editor.execute(1)
        self.song.undo.assert_called_once_with()
        self.assertFalse(self.editor.execute(2))
        self.song.can_redo = True
        self.editor.execute(2)
        self.song.redo.assert_called_once_with()
        for command in (3, 4, 9, 10, 11, 12):
            self.assertFalse(self.editor.execute(command))
        self.clip.view.grid_quantization = -1
        self.editor.modifier(127)
        mask = self.editor.availability(1)
        self.assertEqual(mask & 0xf0, 0)
        self.assertTrue(mask & (1 << 12))
        self.assertFalse(self.editor.execute(5))
        self.assertTrue(self.editor.execute(13))

    def test_redo_availability_tracks_history_after_undo(self):
        self.editor.modifier(127)
        self.assertFalse(self.editor.availability(1) & 2)
        self.song.undo.side_effect = lambda: setattr(self.song, "can_redo", True)
        self.editor.execute(1)
        self.assertTrue(self.editor.availability(1.01) & 2)
        self.song.redo.side_effect = lambda: setattr(self.song, "can_redo", False)
        self.editor.execute(2)
        self.assertFalse(self.editor.availability(1.02) & 2)

    def test_apply_failure_always_closes_undo_step(self):
        self.clip.apply_note_modifications.side_effect = RuntimeError("deleted")
        with self.assertRaises(RuntimeError):
            self.editor.execute(13)
        self.song.end_undo_step.assert_called_once_with()
        self.assertEqual(self.clip.notes[0].pitch, 36)


if __name__ == "__main__":
    unittest.main()
