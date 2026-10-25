import base64
import importlib.util
import pathlib
import types
import unittest


path = pathlib.Path(__file__).parents[1] / "ableton/MikroCompanion/devices.py"
spec = importlib.util.spec_from_file_location("device_test", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class Parameter:
    def __init__(self, name, value=0.5, low=0, high=1, discrete=False, enabled=True):
        self.name, self.value, self.min, self.max = name, value, low, high
        self.is_quantized, self.is_enabled = discrete, enabled

    def __str__(self):
        return "{:.3f} unit".format(self.value)


class Device:
    def __init__(self, name, *parameters):
        self.name, self.parameters = name, parameters


class DeviceTests(unittest.TestCase):
    def test_encoder_reset_uses_live_default_and_retargets_mouse_selection(self):
        self.p.default_value = 0.25
        self.control.reset()
        self.assertEqual(self.p.value, 0.5)
        self.control.toggle()
        self.control.reset()
        self.assertEqual(self.p.value, 0.25)
        self.track.view.selected_device = self.b
        self.b.parameters[0].default_value = -3.0
        self.control.reset()
        self.assertEqual(self.b.parameters[0].value, -3.0)
        self.assertEqual(self.p.value, 0.25)

    def test_reset_does_not_invent_defaults_for_discrete_or_unsupported_parameters(self):
        self.control.toggle()
        self.control.reset()
        self.assertEqual(self.p.value, 0.5)
        self.p.default_value = float('nan')
        self.control.reset()
        self.assertEqual(self.p.value, 0.5)
        self.control.navigate(1)
        self.q.default_value = 0
        self.control.reset()
        self.assertEqual(self.q.value, 1)

    def test_strip_absolute_range_discrete_steps_and_feedback(self):
        self.control.toggle()
        for position, expected in ((0, 0.0), (64, 64 / 127), (127, 1.0)):
            self.control.strip(position)
            self.assertAlmostEqual(self.p.value, expected)
            self.assertEqual(int(self.control.packet(1).split()[11]), position)
        self.p.value = 0.25
        self.assertEqual(int(self.control.packet(1).split()[11]), 32)
        self.control.navigate(1, True)
        self.control.strip(0)
        self.assertEqual(self.b.parameters[0].value, -10)
        self.control.strip(127)
        self.assertEqual(self.b.parameters[0].value, 10)
        self.control.navigate(-1, True)
        self.control.navigate(1)
        for position, expected in ((0, 0), (64, 2), (127, 3)):
            self.control.strip(position)
            self.assertEqual(self.q.value, expected)

    def test_strip_rejects_inactive_invalid_or_deleted_targets(self):
        self.control.strip(0)
        self.assertEqual(self.p.value, 0.5)
        self.control.toggle()
        self.control.strip(-1)
        self.control.strip(128)
        self.assertEqual(self.p.value, 0.5)
        self.p.max = self.p.min
        self.assertEqual(int(self.control.packet(1).split()[11]), -1)
        self.control.strip(127)
        self.assertEqual(self.p.value, 0.5)
        self.track.devices.clear()
        self.control.strip(127)
        self.assertFalse(self.control.enabled)

    def setUp(self):
        self.p = Parameter("Wet")
        self.q = Parameter("Type", 1, 0, 3, True)
        self.a = Device("Filter", self.p, Parameter("Disabled", enabled=False), self.q)
        self.b = Device("Delay", Parameter("Time", 0, -10, 10))
        self.track = types.SimpleNamespace(devices=[self.a, self.b],
                                          view=types.SimpleNamespace(selected_device=self.a))
        self.song = types.SimpleNamespace(view=types.SimpleNamespace(selected_track=self.track))
        self.song.view.select_device = lambda device: setattr(self.track.view, "selected_device", device)
        self.shown = []
        self.control = module.DeviceControl(self.song, lambda: self.shown.append(True))

    def test_entry_and_bounded_navigation_do_not_edit_values(self):
        self.control.turn(1)
        self.assertEqual(self.p.value, 0.5)
        self.control.toggle()
        self.assertTrue(self.control.enabled)
        self.assertEqual(self.control.neighbors(), (False, True))
        self.control.navigate(1)
        self.assertIs(self.control.parameter, self.q)
        self.control.navigate(1)
        self.assertIs(self.control.parameter, self.q)
        self.control.navigate(1, True)
        self.assertIs(self.control.device, self.b)
        self.assertEqual(self.control.neighbors(True), (True, False))
        self.control.navigate(1, True)
        self.assertIs(self.control.device, self.b)
        self.control.navigate(-1, True)
        self.assertIs(self.control.parameter, self.q)
        self.assertEqual((self.p.value, self.q.value), (0.5, 1))
        self.control.toggle()
        self.assertFalse(self.control.enabled)

    def test_relative_steps_fast_mode_and_clamping(self):
        self.control.toggle()
        self.p.value = 0.12345
        self.control.turn(1)
        self.assertAlmostEqual(self.p.value, 0.12345 + 1 / 127)
        self.control.turn(-1)
        self.assertAlmostEqual(self.p.value, 0.12345)
        self.control.turn(1, True)
        self.assertAlmostEqual(self.p.value, 0.12345 + 5 / 127)
        self.control.turn(-1, True)
        self.assertAlmostEqual(self.p.value, 0.12345)
        self.control.turn(1000)
        self.assertEqual(self.p.value, 1)
        self.control.turn(-1000, True)
        self.assertGreaterEqual(self.p.value, 0)
        self.control.navigate(1, True)
        self.control.turn(-1000)
        self.assertEqual(self.b.parameters[0].value, -10)

    def test_discrete_values_advance_five_choices_with_shift(self):
        self.control.toggle()
        self.control.navigate(1)
        self.q.max = 20
        self.control.turn(1, True)
        self.assertEqual(self.q.value, 6)
        self.control.turn(1)
        self.assertEqual(self.q.value, 7)
        self.control.turn(-1, True)
        self.assertEqual(self.q.value, 2)
        self.control.turn(-1, True)
        self.assertEqual(self.q.value, 0)
        self.q.value = 19
        self.control.turn(1, True)
        self.assertEqual(self.q.value, 20)

    def test_mouse_selection_deletion_and_track_change_retarget_before_edit(self):
        self.control.toggle()
        self.track.view.selected_device = self.b
        self.control.turn(1)
        self.assertEqual(self.p.value, 0.5)
        self.assertAlmostEqual(self.b.parameters[0].value, 20 / 127)
        self.track.devices.remove(self.b)
        self.control.turn(1)
        self.assertIs(self.control.device, self.a)
        self.track.devices.clear()
        self.control.packet(1)
        self.assertFalse(self.control.enabled)
        self.assertIsNone(self.control.parameter)

    def test_nested_device_follows_its_selected_chain(self):
        child = Device("Child", Parameter("Gain"))
        child.canonical_parent = types.SimpleNamespace(devices=[child])
        self.track.view.selected_device = child
        self.control.toggle()
        self.assertIs(self.control.device, child)
        self.assertEqual(self.control.neighbors(True), (False, False))

    def test_packet_is_bounded_and_uses_native_formatted_value(self):
        self.control.toggle()
        fields = self.control.packet(99).split()
        self.assertEqual(len(fields), 13)
        self.assertEqual(fields[:8], ["MDEV2", "99", "1", "1", "0", "1", "0", "1"])
        self.assertEqual(base64.b64decode(fields[10]).decode(), "0.500 unit")
        self.a.name = self.p.name = "\U0001f600" * 80
        self.assertLess(len(self.control.packet(99)), 512)


if __name__ == "__main__":
    unittest.main()
