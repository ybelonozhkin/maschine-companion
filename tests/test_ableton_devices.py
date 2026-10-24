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

    def test_relative_steps_fine_mode_and_clamping(self):
        self.control.toggle()
        self.p.value = 0.12345
        self.control.turn(1)
        self.assertAlmostEqual(self.p.value, 0.12345 + 1 / 127)
        self.control.turn(-1)
        self.assertAlmostEqual(self.p.value, 0.12345)
        self.control.turn(1, True)
        self.assertAlmostEqual(self.p.value, 0.12345 + 1 / 1270)
        self.control.turn(1000)
        self.assertEqual(self.p.value, 1)
        self.control.turn(-1000, True)
        self.assertGreaterEqual(self.p.value, 0)
        self.control.navigate(1, True)
        self.control.turn(-1000)
        self.assertEqual(self.b.parameters[0].value, -10)

    def test_discrete_values_advance_once_even_with_shift(self):
        self.control.toggle()
        self.control.navigate(1)
        self.control.turn(1, True)
        self.assertEqual(self.q.value, 2)
        self.control.turn(1)
        self.assertEqual(self.q.value, 3)
        self.control.turn(1)
        self.assertEqual(self.q.value, 3)
        self.control.turn(-1, True)
        self.assertEqual(self.q.value, 2)

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
        self.assertEqual(len(fields), 12)
        self.assertEqual(fields[:8], ["MDEV1", "99", "1", "1", "0", "1", "0", "1"])
        self.assertEqual(base64.b64decode(fields[10]).decode(), "0.500 unit")
        self.a.name = self.p.name = "\U0001f600" * 80
        self.assertLess(len(self.control.packet(99)), 512)


if __name__ == "__main__":
    unittest.main()
