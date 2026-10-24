import base64
import importlib.util
import pathlib
import types
import unittest


path = pathlib.Path(__file__).parents[1] / "ableton/MikroCompanion/navigation.py"
spec = importlib.util.spec_from_file_location("navigation_test", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class Song:
    def __init__(self):
        self.current_song_time = self.start_time = 0.0
        self.signature_numerator, self.signature_denominator = 4, 4
        self.is_playing = False
        self.cue_points = []
        self.start_calls = self.continue_calls = 0

    def start_playing(self):
        self.start_calls += 1
        self.current_song_time = self.start_time
        self.is_playing = True

    def continue_playing(self):
        self.continue_calls += 1
        self.is_playing = True

    def get_current_beats_song_time(self):
        return "17.2.3.0"


class NavigationTests(unittest.TestCase):
    def setUp(self):
        self.song = Song()
        self.nav = module.Navigation(self.song)

    def cue(self, beat, name):
        cue = types.SimpleNamespace(time=beat, name=name)
        def jump():
            self.song.current_song_time = beat
        cue.jump = jump
        self.song.cue_points.append(cue)
        return cue

    def test_position_uses_bars_and_fast_turn_uses_four_bars(self):
        self.nav.turn(1)
        self.assertEqual(self.song.current_song_time, 0)
        self.nav.toggle()
        self.nav.turn(1)
        self.assertEqual(self.song.current_song_time, 4)
        self.nav.turn(2, True)
        self.assertEqual(self.song.current_song_time, 36)
        self.nav.turn(-100)
        self.assertEqual(self.song.current_song_time, 0)
        self.song.signature_numerator, self.song.signature_denominator = 7, 8
        self.nav.turn(1)
        self.assertEqual(self.song.current_song_time, 3.5)
        self.assertFalse(self.song.is_playing)
        self.nav.play()
        self.assertEqual(self.song.current_song_time, 3.5)
        self.assertEqual(self.song.continue_calls, 1)

    def test_navigation_preserves_running_transport(self):
        self.nav.toggle()
        self.song.is_playing = True
        self.song.current_song_time = 10
        self.nav.turn(-1)
        self.assertEqual(self.song.current_song_time, 6)
        self.assertTrue(self.song.is_playing)
        self.assertEqual(self.song.start_calls + self.song.continue_calls, 0)

    def test_markers_no_wrap_no_markers_and_deleted_marker(self):
        self.nav.toggle()
        self.nav.navigate(100)
        self.assertEqual(self.nav.page, 1)
        self.nav.turn(1)
        self.assertIn(base64.b64encode(b"No markers").decode(), self.nav.packet(2))
        first = self.cue(8, "Verse")
        self.cue(24, "Chorus")
        self.nav.packet(2)  # Rendering must not skip the first locator.
        self.nav.turn(1)
        self.assertEqual(self.song.current_song_time, 8)
        self.nav.turn(1, True)
        self.assertEqual(self.song.current_song_time, 24)
        self.nav.turn(1)
        self.assertEqual(self.song.current_song_time, 24)
        self.nav.turn(-1)
        self.assertEqual(self.song.current_song_time, 8)
        self.song.cue_points.remove(first)
        self.nav.turn(1)
        self.assertEqual(self.song.current_song_time, 24)
        self.nav.play()
        self.assertEqual(self.song.current_song_time, 24)
        self.assertEqual(self.song.continue_calls, 1)
        self.nav.toggle()
        self.nav.toggle()
        self.assertEqual(self.nav.page, 1)
        self.nav.navigate(-10)
        self.assertEqual(self.nav.page, 0)

    def test_mouse_start_change_and_restart_override_navigation_start(self):
        self.nav.toggle()
        self.nav.turn(1)
        self.song.start_time = 30
        self.nav.play()
        self.assertEqual(self.song.current_song_time, 30)
        self.assertEqual(self.song.start_calls, 1)
        self.song.is_playing = False
        self.nav.turn(1)
        self.nav.cancel_start()
        self.song.start_time = 0
        self.nav.play()
        self.assertEqual(self.song.current_song_time, 0)

    def test_packet_has_position_and_sanitized_bounded_marker_name(self):
        self.assertEqual(self.nav.packet(9), "MNAV1 9 0 0 - ready")
        self.nav.toggle()
        self.assertEqual(base64.b64decode(self.nav.packet(9).split()[4]).decode(), "17.2.3")
        self.nav.navigate(1)
        self.cue(8, "\n\x01" + "Я" * 100)
        packet = self.nav.packet(9)
        label = base64.b64decode(packet.split()[4]).decode()
        self.assertEqual(label, "Я" * 60)
        self.assertLess(len(packet), 512)


if __name__ == "__main__":
    unittest.main()
