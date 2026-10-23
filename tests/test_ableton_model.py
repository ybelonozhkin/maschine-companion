import importlib.util
import pathlib
import unittest

path = pathlib.Path(__file__).parents[1] / "ableton" / "MikroCompanion" / "model.py"
spec = importlib.util.spec_from_file_location("mikro_model", path)
model = importlib.util.module_from_spec(spec)
spec.loader.exec_module(model)


class ModelTests(unittest.TestCase):
    def test_track_number_is_not_duplicated_in_live_default_names(self):
        for name, prefix, expected in [('1-Grand Piano', '1', '1-Grand Piano'),
                                      ('2 707 Core Kit', '2', '2 707 Core Kit'),
                                      ('Analog', '3', '3 Analog'),
                                      ('808 Kick', '8', '8 808 Kick'),
                                      ('A-Reverb', 'A', 'A-Reverb')]:
            self.assertEqual(model.numbered_track_name(name, prefix), expected)
    def test_partial_end_banks_keep_default_alignment_on_return(self):
        base = 36
        for expected in (20, 4, 0, 0):
            base = model.next_pad_bank(base, -1)
            self.assertEqual(base, expected)
        for expected in (4, 20, 36, 52, 68, 84, 100, 112, 112):
            base = model.next_pad_bank(base, 1)
            self.assertEqual(base, expected)
        self.assertEqual(model.next_pad_bank(112, -1), 100)
        self.assertEqual(model.next_pad_bank(36, -20), 0)

    def test_palette_primary_colors_and_neutral(self):
        for rgb, index in [(0xFF0000, 1), (0x00FF00, 7), (0x0000FF, 11), (0xFFFFFF, 17), (0x808080, 17)]:
            self.assertEqual(model.pad_color(rgb), index)


if __name__ == "__main__":
    unittest.main()
