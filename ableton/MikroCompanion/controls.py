"""MIDI control registration; all controls stay inside component_guard."""

from _Framework.ButtonElement import ButtonElement
from _Framework.InputControlElement import MIDI_CC_TYPE


def register_controls(surface):
    with surface.component_guard():
        surface._play_button = ButtonElement(True, MIDI_CC_TYPE, 0, 20)
        surface._stop_button = ButtonElement(True, MIDI_CC_TYPE, 0, 21)
        surface._record_button = ButtonElement(True, MIDI_CC_TYPE, 0, 22)
        surface._play_button.add_value_listener(surface._play)
        surface._stop_button.add_value_listener(surface._stop)
        surface._record_button.add_value_listener(surface._record)
        surface._bank_down_button = ButtonElement(True, MIDI_CC_TYPE, 0, 23)
        surface._bank_up_button = ButtonElement(True, MIDI_CC_TYPE, 0, 24)
        surface._bank_down_button.add_value_listener(surface._bank_down)
        surface._bank_up_button.add_value_listener(surface._bank_up)
        surface._tap_button = ButtonElement(True, MIDI_CC_TYPE, 0, 25)
        surface._metro_button = ButtonElement(True, MIDI_CC_TYPE, 0, 26)
        surface._tap_button.add_value_listener(surface._tap)
        surface._metro_button.add_value_listener(surface._metronome)
        surface._follow_button = ButtonElement(True, MIDI_CC_TYPE, 0, 27)
        surface._follow_button.add_value_listener(surface._follow)
        surface._select_button = ButtonElement(True, MIDI_CC_TYPE, 0, 28)
        surface._mute_button = ButtonElement(True, MIDI_CC_TYPE, 0, 29)
        surface._solo_button = ButtonElement(True, MIDI_CC_TYPE, 0, 30)
        surface._select_button.add_value_listener(surface._select_note)
        surface._mute_button.add_value_listener(surface._mute)
        surface._solo_button.add_value_listener(surface._solo)
        surface._pad_mode_button = ButtonElement(True, MIDI_CC_TYPE, 0, 31)
        surface._keyboard_button = ButtonElement(True, MIDI_CC_TYPE, 0, 32)
        surface._scale_encoder = ButtonElement(False, MIDI_CC_TYPE, 0, 33)
        surface._scale_toggle_button = ButtonElement(True, MIDI_CC_TYPE, 0, 34)
        surface._pad_mode_button.add_value_listener(surface._pad_mode)
        surface._keyboard_button.add_value_listener(surface._keyboard_mode)
        surface._scale_encoder.add_value_listener(surface._scale_turn)
        surface._scale_toggle_button.add_value_listener(surface._scale_toggle)
        surface._reset_range_button = ButtonElement(True, MIDI_CC_TYPE, 0, 36)
        surface._reset_range_button.add_value_listener(surface._reset_range)
        surface._edit_button = ButtonElement(False, MIDI_CC_TYPE, 0, 37)
        surface._shift_button = ButtonElement(True, MIDI_CC_TYPE, 0, 38)
        surface._edit_button.add_value_listener(surface._edit)
        surface._shift_button.add_value_listener(surface._editor.modifier)
        surface._events_button = ButtonElement(True, MIDI_CC_TYPE, 0, 39)
        surface._event_pad = ButtonElement(False, MIDI_CC_TYPE, 0, 40)
        surface._event_encoder = ButtonElement(False, MIDI_CC_TYPE, 0, 41)
        surface._event_range_encoder = ButtonElement(False, MIDI_CC_TYPE, 0, 42)
        surface._events_button.add_value_listener(lambda value: surface._events_action("toggle") if value else None)
        surface._event_pad.add_value_listener(lambda pitch: surface._events_action("pad", pitch))
        surface._event_encoder.add_value_listener(lambda value: surface._events_action("turn", value if value < 64 else value - 128))
        surface._event_range_encoder.add_value_listener(lambda value: surface._events_action("turn", value if value < 64 else value - 128, True))
        surface._restart_button = ButtonElement(True, MIDI_CC_TYPE, 0, 45)
        surface._loop_button = ButtonElement(True, MIDI_CC_TYPE, 0, 46)
        surface._erase_button = ButtonElement(True, MIDI_CC_TYPE, 0, 47)
        surface._restart_button.add_value_listener(surface._restart)
        surface._loop_button.add_value_listener(surface._loop)
        surface._erase_button.add_value_listener(lambda value: surface._events_action("erase") if value else None)
        surface._volume_button = ButtonElement(True, MIDI_CC_TYPE, 0, 48)
        surface._volume_encoder = ButtonElement(False, MIDI_CC_TYPE, 0, 49)
        surface._track_left_button = ButtonElement(True, MIDI_CC_TYPE, 0, 50)
        surface._track_right_button = ButtonElement(True, MIDI_CC_TYPE, 0, 51)
        surface._volume_button.add_value_listener(lambda value: surface._mixer_action("toggle") if value else None)
        surface._volume_encoder.add_value_listener(lambda value: surface._mixer_action("turn", value if value < 64 else value - 128))
        surface._track_left_button.add_value_listener(lambda value: surface._mixer_action("navigate", -1) if value else None)
        surface._track_right_button.add_value_listener(lambda value: surface._mixer_action("navigate", 1) if value else None)




    with surface.component_guard():
        for cc, action in ((52, lambda v: surface._mixer_action("reset") if v else None),
                           (53, lambda v: surface._mixer_action("fine", v if v < 64 else v - 128)),
                           (54, lambda v: surface._mixer_action("toggle_tempo") if v else None),
                           (55, lambda v: surface._mixer_action("tempo_turn", v if v < 64 else v - 128)),
                           (56, lambda v: surface._mixer_action("tempo_turn", v if v < 64 else v - 128, True)),
                           (57, lambda v: surface._browser_action("toggle") if v else None),
                           (58, lambda v: surface._browser_action("turn", v if v < 64 else v - 128)),
                           (59, lambda v: surface._browser_action("press", 13) if v else None),
                           (60, lambda v: surface._browser_action("press", 37) if v else None),
                           (61, lambda v: surface._step_action("toggle") if v else None),
                           (62, lambda v: surface._step_action("pad", v)),
                           (63, lambda v: surface._step_action("duplicate") if v else None),
                           (64, lambda v: surface._step_action("navigate", v if v < 64 else v - 128)),
                           (65, lambda v: surface._pad_bank_turn(v)),
                           (66, lambda v: surface._keyboard_scale_turn(v)),
                           (67, lambda v: surface._browser_action("close") if v else None),
                           (68, lambda v: surface._browser_action("press", 49) if v else None),
                           (69, lambda v: surface._create_midi_track() if v else None),
                           (70, lambda v: surface._pattern_action("toggle") if v else None),
                           (71, lambda v: surface._pattern_action("pad", v)),
                           (72, lambda v: surface._pattern_action("navigate", v if v < 64 else v - 128)),
                           (73, lambda v: surface._pattern_action("navigate", v if v < 64 else v - 128, True)),
                           (74, lambda v: surface._pattern_action("toggle_scene") if v else None),
                           (75, lambda v: surface._grid_action("grid_hold", bool(v))),
                           (76, lambda v: surface._grid_action("grid_turn", v if v < 64 else v - 128)),
                           (77, lambda v: surface._step_select_turn(v)),
                           (78, lambda v: surface._scale_set(v)),
                           (79, lambda v: surface._step_select_reset() if v else None),
                           (80, lambda v: surface._device_action("toggle") if v else None),
                           (81, lambda v: surface._device_action("navigate", v if v < 64 else v - 128)),
                           (82, lambda v: surface._device_action("navigate", v if v < 64 else v - 128, True)),
                           (83, lambda v: surface._device_action("turn", v if v < 64 else v - 128)),
                           (84, lambda v: surface._device_action("turn", v if v < 64 else v - 128, True)),
                           (85, lambda v: surface._device_action("close") if v else None),
                           (86, lambda v: surface._navigation_action("toggle") if v else None),
                           (87, lambda v: surface._navigation_action("navigate", v if v < 64 else v - 128)),
                           (88, lambda v: surface._navigation_action("turn", v if v < 64 else v - 128)),
                           (89, lambda v: surface._navigation_action("turn", v if v < 64 else v - 128, True)),
                           (90, lambda v: surface._toggle_document_view() if v else None),
                           (91, lambda v: surface._scale_octave(v))):
            button = ButtonElement(False, MIDI_CC_TYPE, 0, cc)
            button.add_value_listener(action)
            setattr(surface, "_encoder_control_{}".format(cc), button)

        surface._pattern_edit = ButtonElement(False, MIDI_CC_TYPE, 0, 92)
        surface._pattern_edit.add_value_listener(lambda value: surface._pattern_action('duplicate' if value == 1 else 'erase') if value in (1, 2) else None)
        surface._device_reset = ButtonElement(True, MIDI_CC_TYPE, 0, 93)
        surface._device_reset.add_value_listener(lambda value: surface._device_action("reset") if value else None)
        surface._device_strip = ButtonElement(False, MIDI_CC_TYPE, 0, 94)
        surface._device_strip.add_value_listener(lambda value: surface._device_action("strip", value))
