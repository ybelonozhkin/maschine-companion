import importlib.util
import pathlib
import types
import unittest
from unittest.mock import Mock

path = pathlib.Path(__file__).parents[1] / "ableton/MikroCompanion/clip_activity.py"
spec = importlib.util.spec_from_file_location("clip_activity_test", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def note(pitch, start, duration=0.05, **values):
    return types.SimpleNamespace(pitch=pitch, start_time=start, duration=duration,
                                 velocity=values.get("velocity", 100), mute=values.get("mute", False),
                                 probability=values.get("probability", 1.0))


class Clip:
    def __init__(self, notes):
        self.notes = notes
        self.callbacks = {}
        self.is_midi_clip = self.is_playing = True
        self.muted = False
        self.playing_position = 0.0
        self.loop_start, self.loop_end, self.length = 0.0, 4.0, 4.0
        self.looping = True
        self.get_notes_extended = Mock(side_effect=lambda *args: self.notes)

    def __getattr__(self, name):
        if name.startswith("add_") and name.endswith("_listener"):
            return lambda callback: self.callbacks.update({name[4:-9]: callback})
        if name.startswith("remove_") and name.endswith("_listener"):
            return lambda callback: self.callbacks.pop(name[7:-9])
        raise AttributeError(name)


class ClipTests(unittest.TestCase):
    def setup_clip(self, notes):
        clip = Clip(notes)
        track = types.SimpleNamespace(playing_slot_index=0,
                                      clip_slots=[types.SimpleNamespace(has_clip=True, clip=clip)], mute=False)
        song = types.SimpleNamespace(is_playing=True, tempo=120.0)
        pulse, log = Mock(), Mock()
        clock = Mock(return_value=10.0)
        activity = module.ClipActivity(pulse, log, clock)
        return activity, clip, track, song, pulse, log, clock

    def test_short_notes_between_display_ticks_and_note_cache(self):
        activity, clip, track, song, pulse, log, clock = self.setup_clip([note(36, 0), note(38, 0.15, 0.01)])
        activity.update(song, track, 36)
        pulse.assert_called_once_with(36)
        clock.return_value = 10.15
        clip.playing_position = 0.25
        activity.update(song, track, 36)
        self.assertEqual([call.args[0] for call in pulse.call_args_list], [36, 38])
        activity.update(song, track, 36)
        self.assertEqual(pulse.call_count, 2)
        clip.get_notes_extended.assert_called_once()
        log.assert_not_called()

    def test_loop_boundary_crosses_tail_and_head_once(self):
        activity, clip, track, song, pulse, _, clock = self.setup_clip([note(36, 0), note(37, 0.1), note(38, 3.95)])
        clip.playing_position = 3.9
        activity.update(song, track, 36)
        clock.return_value = 10.15
        clip.playing_position = 0.2
        activity.update(song, track, 36)
        self.assertEqual(sorted(call.args[0] for call in pulse.call_args_list), [36, 37, 38])

    def test_muted_zero_velocity_and_zero_probability_notes_do_not_flash(self):
        activity, clip, track, song, pulse, _, clock = self.setup_clip([
            note(36, 0.1, mute=True), note(37, 0.1, velocity=0), note(38, 0.1, probability=0), note(39, 0.1)])
        activity.update(song, track, 36)
        clip.playing_position = 0.2
        clock.return_value = 10.1
        activity.update(song, track, 36)
        pulse.assert_called_once_with(39)

    def test_seek_does_not_flash_skipped_notes(self):
        activity, clip, track, song, pulse, _, clock = self.setup_clip([note(36, 1), note(38, 2), note(39, 3)])
        activity.update(song, track, 36)
        clip.playing_position = 3.01
        clock.return_value = 10.1
        activity.update(song, track, 36)
        pulse.assert_called_once_with(39)

    def test_stop_removes_listeners_and_bank_change_uses_new_range(self):
        activity, clip, track, song, pulse, _, clock = self.setup_clip([note(36, 0), note(52, 0)])
        activity.update(song, track, 36)
        activity.update(song, track, 52)
        self.assertEqual([call.args[0] for call in pulse.call_args_list], [36, 52])
        self.assertEqual(clip.get_notes_extended.call_count, 2)
        song.is_playing = False
        activity.update(song, track, 52)
        self.assertEqual(clip.callbacks, {})

    def test_arrangement_clip_and_edits(self):
        activity, clip, track, song, pulse, _, clock = self.setup_clip([note(36, 0.1)])
        track.playing_slot_index = -1
        track.arrangement_clips = [clip]
        activity.update(song, track, 36)
        clip.notes = [note(36, 0.1, mute=True), note(37, 0.1)]
        clip.callbacks["notes"]()
        clock.return_value = 10.1
        clip.playing_position = 0.2
        activity.update(song, track, 36)
        pulse.assert_called_once_with(37)
        activity.disconnect()
        self.assertEqual(clip.callbacks, {})


if __name__ == "__main__":
    unittest.main()
