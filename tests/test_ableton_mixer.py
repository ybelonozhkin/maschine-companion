import base64
import importlib.util
import pathlib
import types
import unittest

path = pathlib.Path(__file__).parents[1] / "ableton/MikroCompanion/mixer.py"
spec = importlib.util.spec_from_file_location("mixer_test", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class Parameter:
    min, max, value, is_enabled = 0.0, 1.0, 0.5, True

    def __str__(self):
        return "-6.0 dB"


def track(name):
    return types.SimpleNamespace(name=name, mixer_device=types.SimpleNamespace(volume=Parameter()))


class MixerTests(unittest.TestCase):
    def setUp(self):
        self.a, self.b, self.ret, self.master = [track(name) for name in ("A", "B", "Return", "Master")]
        self.song = types.SimpleNamespace(tracks=[self.a, self.b], visible_tracks=[self.a, self.b],
                                          return_tracks=[self.ret], master_track=self.master,
                                          view=types.SimpleNamespace(selected_track=self.a))
        self.mixer = module.Mixer(self.song)

    def test_track_navigation_is_bounded_and_includes_returns_master(self):
        self.assertEqual(self.mixer.neighbors(), (False, True))
        self.mixer.navigate(-1)
        self.assertIs(self.song.view.selected_track, self.a)
        for expected in (self.b, self.ret, self.master, self.master):
            self.mixer.navigate(1)
            self.assertIs(self.song.view.selected_track, expected)
        self.assertEqual(self.mixer.neighbors(), (True, False))
        self.mixer.navigate(-1)
        self.assertIs(self.song.view.selected_track, self.ret)

    def test_volume_focus_clamps_parameter_and_keeps_track_navigation(self):
        self.mixer.turn(1)
        self.assertEqual(self.a.mixer_device.volume.value, 0.5)
        self.mixer.toggle()
        self.mixer.turn(1)
        self.assertAlmostEqual(self.a.mixer_device.volume.value, 0.505)
        self.mixer.turn(1000)
        self.assertEqual(self.a.mixer_device.volume.value, 1)
        self.mixer.navigate(1)
        self.assertTrue(self.mixer.enabled)
        self.mixer.turn(-1000)
        self.assertEqual(self.b.mixer_device.volume.value, 0)
        self.mixer.toggle()
        self.assertFalse(self.mixer.enabled)

    def test_zero_db_reset_and_whole_db_steps_use_native_curve(self):
        class Fader(Parameter):
            default_value = 0.75
            def str_for_value(self, value):
                return "{:.5f} dB".format(80 * value * value - 45)
            def __str__(self):
                return self.str_for_value(self.value)
        fader = Fader()
        self.a.mixer_device.volume = fader
        self.mixer.reset()
        self.assertEqual(fader.value, 0.5)
        self.mixer.toggle()
        self.mixer.reset()
        self.assertEqual(fader.value, 0.75)
        self.mixer.coarse(-1)
        self.assertAlmostEqual(module.Mixer.decibels(str(fader)), -1.0, places=4)
        self.mixer.coarse(2)
        self.assertAlmostEqual(module.Mixer.decibels(str(fader)), 1.0, places=4)
        self.mixer.coarse(500)
        self.assertAlmostEqual(fader.value, 1.0)

    def test_tempo_overlay_is_exclusive_and_bounded(self):
        self.song.tempo = 120.0
        self.mixer.toggle()
        self.mixer.toggle_tempo()
        self.assertFalse(self.mixer.enabled)
        self.mixer.tempo_turn(1)
        self.assertEqual(self.song.tempo, 121)
        self.mixer.tempo_turn(-2, True)
        self.assertEqual(self.song.tempo, 119)
        self.song.tempo = 123.6
        self.mixer.tempo_turn(1)
        self.assertEqual(self.song.tempo, 125)
        self.mixer.tempo_turn(5000)
        self.assertEqual(self.song.tempo, 999)
        self.mixer.tempo_turn(-5000, True)
        self.assertEqual(self.song.tempo, 20)
        self.mixer.toggle()
        self.assertFalse(self.mixer.tempo)

    def test_native_infinity_format_is_readable_on_oled(self):
        class Silent(Parameter):
            def __str__(self):
                return "\u2212\u221e dB"
        self.a.mixer_device.volume = Silent()
        fields = self.mixer.packet(1).split()
        self.assertEqual(base64.b64decode(fields[6]).decode(), "-INF dB")

    def test_display_uses_live_formatted_string_and_disabled_state(self):
        self.mixer.toggle()
        fields = self.mixer.packet(20).split()
        self.assertEqual(fields[:6], ["MX1", "20", "1", "1", "0", "1"])
        self.assertEqual(base64.b64decode(fields[6]).decode(), "-6.0 dB")
        self.a.mixer_device.volume.is_enabled = False
        self.assertEqual(self.mixer.packet(20).split()[2:4], ["0", "0"])
        self.assertFalse(self.mixer.enabled)


if __name__ == "__main__":
    unittest.main()
