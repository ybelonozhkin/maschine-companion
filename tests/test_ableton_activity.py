import importlib.util
import pathlib
import unittest
from unittest.mock import Mock

path = pathlib.Path(__file__).parents[1] / "ableton/MikroCompanion/activity.py"
spec = importlib.util.spec_from_file_location("sampler_activity", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class Sampler:
    def __init__(self):
        self.enabled = False
        self.position = 0.0
        self.deleted = False
        self.callbacks = {}

    @property
    def playing_position_enabled(self):
        if self.deleted:
            raise TypeError("deleted device")
        return self.enabled

    @property
    def playing_position(self):
        return self.position


class ActivityTests(unittest.TestCase):
    def make_watch(self):
        device = Sampler()
        pulse, error = Mock(), Mock()
        activity = module.SamplerActivity(lambda obj, name, cb: obj.callbacks.update({name: cb}), pulse, error)
        activity.watch(device, 36)
        return device, pulse, error

    def test_playback_start_and_retrigger_pulse_without_sample_progress_flashes(self):
        device, pulse, error = self.make_watch()
        pulse.assert_not_called()
        device.enabled = True
        device.callbacks["playing_position_enabled"]()
        pulse.assert_called_once_with(36)
        for position in [0.1, 0.3, 0.8]:
            device.position = position
            device.callbacks["playing_position"]()
        self.assertEqual(pulse.call_count, 1)
        device.position = 0.0
        device.callbacks["playing_position"]()
        self.assertEqual(pulse.call_count, 2)
        error.assert_not_called()

    def test_sample_end_does_not_pulse_and_next_start_does(self):
        device, pulse, _ = self.make_watch()
        device.enabled = True
        device.callbacks["playing_position_enabled"]()
        device.enabled = False
        device.callbacks["playing_position_enabled"]()
        device.position = 0.0
        device.callbacks["playing_position"]()
        self.assertEqual(pulse.call_count, 1)
        device.enabled = True
        device.callbacks["playing_position_enabled"]()
        self.assertEqual(pulse.call_count, 2)

    def test_deleted_device_does_not_throw_or_flood_logs(self):
        device, pulse, error = self.make_watch()
        device.deleted = True
        for _ in range(3):
            device.callbacks["playing_position"]()
        pulse.assert_not_called()
        error.assert_called_once_with("deleted device")

    def test_position_jitter_does_not_retrigger(self):
        device, pulse, _ = self.make_watch()
        device.enabled = True
        device.position = 0.5
        device.callbacks["playing_position_enabled"]()
        device.position = 0.49999
        device.callbacks["playing_position"]()
        self.assertEqual(pulse.call_count, 1)


if __name__ == "__main__":
    unittest.main()
