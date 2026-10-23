import importlib.util
import pathlib
import types
import unittest
from unittest.mock import patch

path = pathlib.Path(__file__).parents[1] / "ableton/MikroCompanion/keyboard.py"
spec = importlib.util.spec_from_file_location("keyboard_test", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class KeyboardTests(unittest.TestCase):
    def song(self):
        return types.SimpleNamespace(root_note=0, scale_name="Major", scale_mode=True,
                                     scale_intervals=(0, 2, 4, 5, 7, 9, 11), tracks=[])

    def test_major_minor_and_chromatic_are_dense_and_in_order(self):
        self.assertEqual(module.layout(36, 0, (0, 2, 4, 5, 7, 9, 11), True),
                         (36, 38, 40, 41, 43, 45, 47, 48, 50, 52, 53, 55, 57, 59, 60, 62))
        self.assertEqual(module.layout(36, 0, (0, 2, 3, 5, 7, 8, 10), True)[:8],
                         (36, 38, 39, 41, 43, 44, 46, 48))
        self.assertEqual(module.layout(37, 0, (), False), tuple(range(37, 53)))

    def test_start_moves_without_transposing_scale_and_handles_upper_edge(self):
        song = self.song()
        keyboard = module.Keyboard()
        keyboard.starts["track"] = 37
        self.assertEqual(keyboard.notes(song, "track")[:3], (36, 38, 40))
        self.assertEqual(song.root_note, 0)
        self.assertEqual(module.layout(127, 0, (0,), True), (-1,) * 16)
        self.assertEqual(module.layout(127, 7, (0,), True), (127,) + (-1,) * 15)

    def test_root_encoder_moves_first_pad_and_preserves_octaves(self):
        song = self.song()
        song.view = types.SimpleNamespace(selected_track="a")
        keyboard = module.Keyboard()
        keyboard.enabled = True
        keyboard.starts["a"] = 47
        song.root_note = 11
        keyboard.adjust(song, 1)
        self.assertEqual((song.root_note, keyboard.notes(song, "a")[0]), (0, 48))
        keyboard.adjust(song, -1)
        self.assertEqual((song.root_note, keyboard.notes(song, "a")[0]), (11, 47))
        self.assertTrue(song.scale_mode)
        self.assertFalse(hasattr(keyboard, "page"))
        song.root_note = 2
        self.assertEqual(keyboard.notes(song, "a")[0], 38)

    def test_catalog_order_and_no_wrap(self):
        song = self.song()
        keyboard = module.Keyboard()
        keyboard.enabled = True
        catalog = (("Major", (0, 2, 4)), ("Minor", (0, 2, 3)), ("Dorian", (0, 2, 3, 5)))
        with patch.object(module, "scale_catalog", return_value=catalog) as source:
            keyboard.turn(song, 1)
            self.assertEqual(song.scale_name, "Minor")
            keyboard.turn(song, 1)
            self.assertEqual(song.scale_name, "Dorian")
            keyboard.turn(song, 1)
            self.assertEqual(song.scale_name, "Dorian")
            keyboard.turn(song, -1)
            self.assertEqual(song.scale_name, "Minor")
            source.assert_called_once_with()
        keyboard.enabled = False
        keyboard.turn(song, 1)
        self.assertEqual(song.scale_name, "Minor")

    def test_packet_tracks_live_state_and_independent_starts(self):
        song = self.song()
        song.tracks = ["a", "b"]
        keyboard = module.Keyboard()
        keyboard.enabled = True
        keyboard.starts["a"] = 48
        self.assertEqual(keyboard.start(song, "b"), 36)
        fields = keyboard.packet(song, "a", 99).split()
        self.assertEqual(fields[:6], ["MK2", "99", "1", "1", "0", "48"])
        song.scale_mode = False
        self.assertEqual(keyboard.notes(song, "a"), tuple(range(48, 64)))
        song.root_note = 2
        self.assertEqual(keyboard.start(song, "b"), 38)
        song.tracks = ["b"]
        keyboard.packet(song, "b", 100)
        self.assertNotIn("a", keyboard.starts)


if __name__ == "__main__":
    unittest.main()
