import unittest
from unittest.mock import Mock

import test_ableton_surface as support

LiveNode = support.LiveNode
PatternGrid = support.surface_module.PatternGrid


class PatternTests(unittest.TestCase):
    def make_grid(self, tracks=6, scenes=9):
        rows = [LiveNode(color=0x00FF00, is_triggered=False, fire=Mock()) for _ in range(scenes)]
        channels = []
        for channel in range(tracks):
            slots = []
            for scene in range(scenes):
                clip = LiveNode(color=0xFF0000, is_playing=False, is_recording=False, is_triggered=False)
                slots.append(LiveNode(has_clip=True, clip=clip, set_fire_button_state=Mock()))
            channels.append(LiveNode(clip_slots=slots, playing_slot_index=-1, stop_all_clips=Mock()))
        for index, row in enumerate(rows):
            row.clip_slots = [track.clip_slots[index] for track in channels]
        for track in channels:
            for slot in track.clip_slots:
                slot.canonical_parent = track
                slot.clip.canonical_parent = track
        song = LiveNode(tracks=channels, visible_tracks=channels, scenes=rows, is_playing=True,
                        view=LiveNode(selected_track=channels[0], selected_scene=rows[0]))
        highlight = Mock()
        return PatternGrid(song, highlight), song, highlight

    def test_orientation_launch_release_and_empty_slot(self):
        grid, song, highlight = self.make_grid()
        grid.enter()
        highlight.assert_called_with(0, 0, 4, 4, False)
        grid.pad(12)  # Physical top-left pad: first scene, first track.
        slot = song.tracks[0].clip_slots[0]
        slot.set_fire_button_state.assert_called_once_with(True)
        grid.pad(12)  # Duplicate reports must not retrigger.
        grid.pad(28)
        self.assertEqual([call.args[0] for call in slot.set_fire_button_state.call_args_list], [True, False])
        grid.pad(3)  # Bottom-right: fourth scene, fourth track.
        song.tracks[3].clip_slots[3].set_fire_button_state.assert_called_once_with(True)
        empty = song.tracks[1].clip_slots[0]
        empty.has_clip = False
        grid.pad(13)
        empty.set_fire_button_state.assert_not_called()
        grid.close()
        song.tracks[3].clip_slots[3].set_fire_button_state.assert_called_with(False)
        highlight.assert_called_with(-1, -1, -1, -1, False)

    def test_bounded_navigation_and_deleted_tracks(self):
        grid, song, highlight = self.make_grid()
        grid.enter()
        grid.pad(12)
        grid.navigate(20)
        self.assertEqual(grid.scene, 5)
        song.tracks[0].clip_slots[0].set_fire_button_state.assert_called_with(False)
        grid.navigate(1, True)
        self.assertEqual(grid.track, 2)
        grid.navigate(-100, True)
        grid.navigate(-100)
        self.assertEqual((grid.track, grid.scene), (0, 0))
        song.visible_tracks = song.tracks[:1]
        song.scenes = song.scenes[:1]
        self.assertEqual(len(grid.packet(7).split()), 31)
        self.assertIsNone(grid.slot(15))

    def test_packet_colors_playback_and_recording(self):
        grid, song, _ = self.make_grid()
        grid.enter()
        song.tracks[0].clip_slots[0].clip.is_playing = True
        song.tracks[1].clip_slots[0].clip.is_triggered = True
        song.tracks[2].clip_slots[0].clip.is_recording = True
        song.tracks[3].clip_slots[0].has_clip = False
        fields = grid.packet(9).split()
        self.assertEqual(fields[:7], ['MPT5', '9', '1', '0', '0', '0', '1'])
        self.assertEqual(tuple(map(int, fields[7:10])), (1 << 12, 1 << 13, 1 << 14))
        self.assertEqual(fields[11+12:11+16], ['1', '1', '1', '0'])

    def test_stop_targets_column_even_when_playing_clip_is_outside_grid(self):
        grid, song, _ = self.make_grid()
        song.tracks[1].playing_slot_index = 8
        song.is_playing = True
        grid.enter()
        fields = grid.packet(9).split()
        self.assertEqual(fields[27], '2')
        grid.pad(33)
        song.tracks[1].stop_all_clips.assert_called_once_with()
        for index, track in enumerate(song.tracks):
            if index != 1:
                track.stop_all_clips.assert_not_called()
        self.assertTrue(song.is_playing)
        song.is_playing = False
        self.assertEqual(grid.packet(9).split()[27], '0')
        song.is_playing = True
        for slot in song.tracks[1].clip_slots:
            slot.set_fire_button_state.assert_not_called()
        grid.pad(17)  # Release cannot launch the clip under the Stop pad.
        grid.navigate(1, True)
        grid.pad(32)
        song.tracks[2].stop_all_clips.assert_called_once_with()
        grid.close()
        grid.pad(32)
        self.assertEqual(song.tracks[2].stop_all_clips.call_count, 1)

    def test_selectable_cells_include_empty_slots_but_not_missing_tracks_or_scenes(self):
        for tracks in (1, 2, 3):
            for scenes in (1, 2, 4):
                with self.subTest(tracks=tracks, scenes=scenes):
                    grid, song, _ = self.make_grid(tracks=tracks, scenes=scenes)
                    song.tracks[0].clip_slots[0].has_clip = False
                    grid.enter()
                    expected = sum(1 << ((3 - row) * 4 + column)
                                   for row in range(scenes) for column in range(tracks))
                    self.assertEqual(int(grid.packet(9).split()[29]), expected)
                    grid.select(12)
                    self.assertIs(song.view.highlighted_clip_slot, song.tracks[0].clip_slots[0])
                    self.assertFalse(any(slot.set_fire_button_state.called
                                         for track in song.tracks for slot in track.clip_slots))
                    # Scene removal can precede changes to the track's slot collection.
                    song.scenes = song.scenes[:1]
                    self.assertIsNone(grid.slot(8))
                    self.assertEqual(int(grid.packet(9).split()[29]), ((1 << tracks) - 1) << 12)
                    grid.enter(True)
                    self.assertEqual(int(grid.packet(9).split()[29]), 0)

    def test_stop_padding_and_upper_row_clip_launch(self):
        grid, song, _ = self.make_grid(tracks=2)
        grid.enter()
        grid.pad(35)  # No fourth track.
        self.assertFalse(any(track.stop_all_clips.called for track in song.tracks))
        grid.pad(12)
        song.tracks[0].clip_slots[0].set_fire_button_state.assert_called_once_with(True)

    def test_toggle_restores_previous_mode_and_direct_layout_exits(self):
        helper = support.SurfaceTests()
        song = helper.song_with_track()
        surface, _ = helper.make_surface(song)
        surface.application().view.show_view = Mock()
        surface._keyboard.enabled = True
        surface._pattern_action('toggle')
        self.assertTrue(surface._pattern.enabled)
        surface.application().view.show_view.assert_called_with('Session')
        surface._pattern_action('toggle')
        self.assertFalse(surface._pattern.enabled)
        self.assertTrue(surface._keyboard.enabled)
        surface._pattern_action('toggle')
        surface._pad_mode(127)
        self.assertFalse(surface._pattern.enabled)
        self.assertFalse(surface._keyboard.enabled)

    def test_scene_launch_orientation_and_shared_viewport(self):
        grid, song, _ = self.make_grid()
        grid.enter()
        grid.navigate(3)
        grid.enter(True)
        self.assertEqual(grid.scene, 3)
        grid.pad(40)
        grid.pad(43)
        song.scenes[3].fire.assert_called_once_with()
        song.scenes[6].fire.assert_called_once_with()
        grid.pad(12)
        grid.pad(32)
        for track in song.tracks:
            track.stop_all_clips.assert_not_called()
            for slot in track.clip_slots:
                slot.set_fire_button_state.assert_not_called()
        song.tracks[5].clip_slots[3].clip.is_playing = True
        song.scenes[4].is_triggered = True
        fields = grid.packet(9).split()
        self.assertEqual(fields[2], '2')
        colors = list(map(int, fields[11:27]))
        self.assertEqual([i for i, c in enumerate(colors) if c], [3, 7, 11, 15])
        self.assertEqual(int(fields[7]), 1 << 15)
        self.assertEqual(int(fields[8]), 1 << 11)
        grid.navigate(1)
        grid.enter(False)
        self.assertEqual(grid.scene, 4)

    def test_scene_toggle_returns_to_pattern_without_moving_grid(self):
        helper = support.SurfaceTests()
        surface, _ = helper.make_surface(helper.song_with_track())
        surface.application().view.show_view = Mock()
        surface._pattern_action('toggle')
        surface._pattern_action('toggle_scene')
        self.assertTrue(surface._pattern.scene_mode)
        surface._pattern_action('toggle_scene')
        self.assertTrue(surface._pattern.enabled)
        self.assertFalse(surface._pattern.scene_mode)

    def test_empty_slot_selects_destination_without_firing_or_arming(self):
        grid, song, _ = self.make_grid()
        grid.enter()
        slot = song.tracks[2].clip_slots[1]
        slot.has_clip = False
        song.tracks[2].arm = False
        class StrictView(LiveNode):
            def __setattr__(self, name, value):
                if name == 'detail_clip' and value is None:
                    raise TypeError('Live requires a clip handle')
                super().__setattr__(name, value)
        old_clip = song.tracks[0].clip_slots[0].clip
        song.view = StrictView(selected_track=song.tracks[0], selected_scene=song.scenes[0], detail_clip=old_clip)
        grid.pad(10)
        self.assertIs(song.view.highlighted_clip_slot, slot)
        self.assertIs(song.view.selected_track, song.tracks[2])
        self.assertIs(song.view.selected_scene, song.scenes[1])
        self.assertIs(song.view.detail_clip, old_clip)
        self.assertFalse(song.tracks[2].arm)
        slot.set_fire_button_state.assert_not_called()
        packet = grid.packet(9).split()
        self.assertEqual(packet[11+10], '17')
        self.assertEqual(packet[28], '10')
        song.tracks[2].has_midi_input = True
        sequencer = support.surface_module.Sequencer(song, lambda: False, lambda: True)
        sequencer.toggle(36)
        self.assertTrue(sequencer.enabled)
        self.assertIs(sequencer.slot, slot)
        self.assertIsNone(sequencer.clip)
        slot.set_fire_button_state.assert_not_called()

    def test_select_filled_clip_opens_editor_without_launch_and_plain_pad_still_launches(self):
        grid, song, _ = self.make_grid()
        grid.show_clip = Mock()
        grid.enter()
        slot = song.tracks[1].clip_slots[0]
        grid.pad(48+13)
        self.assertIs(song.view.detail_clip, slot.clip)
        self.assertIs(song.view.highlighted_clip_slot, slot)
        grid.show_clip.assert_called_once_with()
        slot.set_fire_button_state.assert_not_called()
        grid.pad(13)
        slot.set_fire_button_state.assert_called_once_with(True)

    def test_step_returns_to_origin_grid_and_explicit_pad_mode_discards_return(self):
        for origin in ('toggle', 'toggle_scene'):
            with self.subTest(origin=origin):
                helper = support.SurfaceTests()
                song = helper.song_with_track()
                track = song.view.selected_track
                song.view.detail_clip = LiveNode(is_midi_clip=True, canonical_parent=track,
                    looping=True, loop_start=0.0, loop_end=4.0, is_playing=False, color=0xFF0000,
                    get_all_notes_extended=Mock(return_value=[]))
                surface, _ = helper.make_surface(song)
                surface.application().view.show_view = Mock()
                surface.application().view.is_view_visible = Mock(return_value=True)
                surface._pattern_action(origin)
                surface._step_action('toggle')
                self.assertTrue(surface._sequencer.enabled)
                self.assertFalse(surface._pattern.enabled)
                surface._step_action('toggle')
                self.assertTrue(surface._pattern.enabled)
                self.assertEqual(surface._pattern.scene_mode, origin == 'toggle_scene')
                surface._step_action('toggle')
                surface.application().view.is_view_visible.return_value = False
                surface.update_display()
                self.assertTrue(surface._pattern.enabled)
                self.assertEqual(surface._pattern.scene_mode, origin == 'toggle_scene')
                surface.application().view.is_view_visible.return_value = True
                surface._step_action('toggle')
                surface._pad_mode(127)
                self.assertIsNone(surface._step_return_grid)
                surface._step_action('toggle')
                surface._step_action('toggle')
                self.assertFalse(surface._pattern.enabled)

    def test_empty_selection_keeps_pattern_active(self):
        helper = support.SurfaceTests()
        song = helper.song_with_track()
        track = song.view.selected_track
        track.has_midi_input = True
        song.scenes = [LiveNode() for _ in range(4)]
        track.clip_slots = [LiveNode(has_clip=False, canonical_parent=track) for _ in range(4)]
        surface, _ = helper.make_surface(song)
        surface.application().view.show_view = Mock()
        surface._pattern_action('toggle')
        surface._pattern_action('pad', 48+12)
        self.assertTrue(surface._pattern.enabled)
        self.assertIs(song.view.highlighted_clip_slot, track.clip_slots[0])
        surface._step_action('toggle')
        self.assertTrue(surface._sequencer.enabled)
        surface._step_action('toggle')
        self.assertTrue(surface._pattern.enabled)


if __name__ == '__main__':
    unittest.main()
