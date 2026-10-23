import importlib.util
import pathlib
import types
import unittest
from unittest.mock import Mock

path = pathlib.Path(__file__).parents[1] / "ableton/MikroCompanion/browser.py"
spec = importlib.util.spec_from_file_location("browser_test", path)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class BrowserTests(unittest.TestCase):
    def test_browser_focus_and_encoder_commands(self):
        view = Mock()
        send_key = Mock()
        browser = module.Browser(types.SimpleNamespace(view=view), send_key)
        browser.turn(1)
        send_key.assert_not_called()
        browser.toggle()
        view.show_view.assert_called_with("Browser")
        view.focus_view.assert_called_with("Browser")
        browser.turn(2)
        browser.press(13)
        browser.press(37)
        self.assertEqual([c.args[0] for c in send_key.call_args_list], [256, 40, 40, 13, 37])
        self.assertEqual(view.focus_view.call_count, 1)
        browser.press(65)
        self.assertEqual(send_key.call_count, 5)
        browser.toggle()
        browser.press(13)
        self.assertEqual(send_key.call_count, 5)

    def test_fast_scroll_and_favorite_command_and_explicit_close(self):
        send_key = Mock()
        browser = module.Browser(types.SimpleNamespace(view=Mock()), send_key)
        browser.toggle()
        browser.turn(-6)
        browser.press(49)
        self.assertEqual([c.args[0] for c in send_key.call_args_list], [256, 1018, 49])
        browser.close()
        browser.turn(1)
        browser.close()
        self.assertFalse(browser.enabled)
        self.assertEqual(send_key.call_count, 3)

    def test_fast_scroll_is_one_jump_in_either_direction(self):
        send_key = Mock()
        browser = module.Browser(types.SimpleNamespace(view=Mock()), send_key)
        browser.toggle()
        browser.turn(6)
        browser.turn(-12)
        self.assertEqual([c.args[0] for c in send_key.call_args_list], [256, 1030, 1012])

    def test_commands_do_not_guess_focus_from_click_history(self):
        send_key = Mock()
        browser = module.Browser(types.SimpleNamespace(view=Mock()), send_key)
        browser.toggle()
        browser.press(37)
        browser.turn(2)
        browser.press(13)
        browser.turn(1)
        browser.press(13)
        self.assertEqual([c.args[0] for c in send_key.call_args_list], [256, 37, 40, 40, 13, 40, 13])
        browser.press(37)
        browser.press(37)
        browser.press(13)
        browser.press(13)
        self.assertEqual([c.args[0] for c in send_key.call_args_list][-4:], [37, 37, 13, 13])

    def test_load_without_ctypes_in_embedded_python(self):
        import builtins
        from unittest.mock import patch
        native_import = builtins.__import__
        def embedded_import(name, *args, **kwargs):
            if name == "ctypes" or name.startswith("ctypes."):
                raise ModuleNotFoundError("No module named 'ctypes'")
            return native_import(name, *args, **kwargs)
        clean = importlib.util.module_from_spec(spec)
        with patch("builtins.__import__", embedded_import):
            spec.loader.exec_module(clean)
            browser = clean.Browser(types.SimpleNamespace(view=Mock()), Mock())
            browser.toggle()
            browser.press(13)
