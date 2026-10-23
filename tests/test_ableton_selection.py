import importlib.util
import pathlib
import sys
import types
import unittest
from unittest.mock import Mock

folder = pathlib.Path(__file__).parents[1] / "ableton/MikroCompanion"
package = types.ModuleType("selection_tests")
package.__path__ = [str(folder)]
sys.modules["selection_tests"] = package
spec = importlib.util.spec_from_file_location("selection_tests.selection", folder / "selection.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def note(ident, pitch, time):
    return types.SimpleNamespace(note_id=ident, pitch=pitch, start_time=time)


class Clip:
    is_midi_clip = True

    def __init__(self, track):
        self.canonical_parent = types.SimpleNamespace(canonical_parent=track)
        self.notes = [note(1, 36, 0.0), note(2, 38, 0.25), note(3, 36, 0.5), note(4, 36, 0.75)]
        self.selected = set()

    def get_all_notes_extended(self):
        return self.notes

    def get_selected_notes_extended(self):
        return [n for n in self.notes if n.note_id in self.selected]

    def deselect_all_notes(self):
        self.selected.clear()

    def select_notes_by_id(self, ids):
        self.selected.update(ids)


class SelectionTests(unittest.TestCase):
    def setUp(self):
        self.track = object()
        self.clip = Clip(self.track)
        self.song = types.SimpleNamespace(view=types.SimpleNamespace(selected_track=self.track, detail_clip=self.clip))
        self.visible = True
        self.selection = module.NoteSelection(self.song, lambda: self.visible)

    def test_pad_groups_toggle_without_changing_notes(self):
        self.selection.toggle()
        self.selection.pad(36)
        self.assertEqual(self.clip.selected, {1, 3, 4})
        self.selection.pad(38)
        self.assertEqual(self.clip.selected, {1, 2, 3, 4})
        self.selection.pad(36)
        self.assertEqual(self.clip.selected, {2})
        self.selection.pad(50)
        self.assertEqual(self.clip.selected, {2})
        self.assertEqual([n.pitch for n in self.clip.notes], [36, 38, 36, 36])

    def test_wheel_range_and_plain_turn_replaces_range(self):
        self.selection.toggle()
        self.selection.pad(36)
        self.selection.pad(38)
        self.selection.turn(1)
        self.assertEqual(self.clip.selected, {1})
        self.selection.turn(1, True)
        self.assertEqual(self.clip.selected, {1, 2})
        self.selection.turn(1, True)
        self.assertEqual(self.clip.selected, {1, 2, 3})
        self.selection.turn(-1, True)
        self.assertEqual(self.clip.selected, {1, 2})
        # Releasing Shift alone performs no selection operation.
        self.assertEqual(self.clip.selected, {1, 2})
        self.selection.turn(1)
        self.assertEqual(self.clip.selected, {3})
        self.selection.turn(1)
        self.selection.turn(1)
        self.assertEqual(self.clip.selected, {4})

    def test_exit_keeps_live_selection_and_track_change_exits(self):
        self.selection.toggle()
        self.selection.pad(36)
        self.selection.toggle()
        self.assertEqual(self.clip.selected, {1, 3, 4})
        self.selection.toggle()
        self.song.view.selected_track = object()
        self.selection.sync()
        self.assertFalse(self.selection.enabled)
        self.assertFalse(self.selection.available)

    def test_closed_editor_audio_and_other_clip_on_same_track(self):
        self.selection.toggle()
        self.selection.pad(36)
        second = Clip(self.track)
        self.song.view.detail_clip = second
        self.selection.sync()
        self.assertTrue(self.selection.enabled)
        self.assertEqual(self.selection.pitches, set())
        self.assertEqual(self.clip.selected, {1, 3, 4})
        self.visible = False
        self.selection.sync()
        self.assertFalse(self.selection.enabled)
        self.selection.toggle()
        self.assertFalse(self.selection.enabled)
        self.visible = True
        second.is_midi_clip = False
        self.selection.toggle()
        self.assertFalse(self.selection.enabled)

    def test_erase_deletes_only_native_selection_and_is_one_undo_step(self):
        self.song.begin_undo_step, self.song.end_undo_step = Mock(), Mock()
        self.clip.remove_notes_by_id = Mock(side_effect=lambda ids: setattr(self.clip, "notes", [n for n in self.clip.notes if n.note_id not in ids]))
        self.selection.toggle()
        self.assertFalse(self.selection.can_erase())
        self.selection.erase()
        self.clip.remove_notes_by_id.assert_not_called()
        self.selection.pad(36)
        self.selection.turn(1)
        self.assertEqual(self.clip.selected, {1})
        self.assertTrue(self.selection.can_erase())
        self.selection.erase()
        self.assertEqual([n.note_id for n in self.clip.notes], [2, 3, 4])
        self.clip.remove_notes_by_id.assert_called_once_with((1,))
        self.song.begin_undo_step.assert_called_once_with()
        self.song.end_undo_step.assert_called_once_with()
        self.selection.enabled = False
        self.selection.erase()
        self.assertEqual(self.clip.remove_notes_by_id.call_count, 1)

    def test_selection_masks_follow_pad_mapping_and_deleted_events(self):
        self.selection.toggle()
        self.selection.pad(38)
        self.assertEqual(self.selection.masks([36, 38, 40, -1]), (3, 2))
        self.clip.notes = [n for n in self.clip.notes if n.pitch != 38]
        self.selection.sync()
        self.assertEqual(self.selection.masks([36, 38, 40]), (1, 0))
        self.selection.turn(1)
        self.assertEqual(self.selection.pitches, set())


if __name__ == "__main__":
    unittest.main()
