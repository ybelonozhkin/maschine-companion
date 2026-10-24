import importlib.util
import pathlib
from types import SimpleNamespace as Node
import unittest

spec = importlib.util.spec_from_file_location('ownership', pathlib.Path(__file__).parents[1] / 'ableton/MikroCompanion/ownership.py')
ownership = importlib.util.module_from_spec(spec)
spec.loader.exec_module(ownership)


class Device:
    def __init__(self, original='Operator', name='Renamed', **values):
        self.class_display_name = original
        self.class_name = 'PluginDevice'
        self.name = name
        self.can_have_chains = False
        self.__dict__.update(values)


class Tests(unittest.TestCase):
    def song(self, *tracks):
        return Node(tracks=tracks, view=Node(selected_track=tracks[0]))

    def test_renamed_maschine_keeps_original_identity(self):
        song = self.song(Node(devices=[Device('Maschine 3', name='Synth')]))
        self.assertEqual(ownership.packet(song, 7), 'MO1 7 1 1 0 ready')

    def test_renaming_another_plugin_does_not_claim_native_ownership(self):
        self.assertEqual(ownership.inspect_song(self.song(Node(devices=[Device('Serum', name='Maschine 3')]))), (False, 0, False))

    def test_counts_instances_outside_selected_track_in_nested_racks(self):
        other = Node(devices=[Device('Rack', can_have_chains=True, chains=[Node(devices=[Device('Maschine 2')])])])
        self.assertEqual(ownership.inspect_song(self.song(Node(devices=[]), other)), (False, 1, False))

    def test_multiple_instances_including_drum_return(self):
        rack = Device('Drum Rack', can_have_chains=True, chains=[Node(devices=[Device('Maschine 3')])], return_chains=[Node(devices=[Device('Maschine 3')])])
        self.assertEqual(ownership.inspect_song(self.song(Node(devices=[rack]))), (True, 2, False))

    def test_ambiguous_plugin_metadata_blocks_takeover(self):
        self.assertEqual(ownership.inspect_song(self.song(Node(devices=[Device('PluginDevice')]))), (False, 0, True))

    def test_deleted_handle_blocks_takeover(self):
        class Deleted:
            @property
            def devices(self):
                raise RuntimeError('deleted')
        self.assertEqual(ownership.inspect_song(self.song(Deleted())), (False, 0, True))

    def test_bounds_graph_and_handles_cycles(self):
        chain = Node(devices=[])
        rack = Device('Rack', can_have_chains=True, chains=[chain])
        chain.devices.append(rack)
        self.assertEqual(ownership.inspect_song(self.song(Node(devices=[rack]))), (False, 0, False))


if __name__ == '__main__':
    unittest.main()
