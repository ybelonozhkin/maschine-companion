import math

from .editing import all_notes, target_clip, grid_step
from .model import pad_color


class Sequencer:
    STEP = 0.25
    GRIDS = (("1/4", 1.0, "g_quarter", False), ("1/4T", 2.0/3, "g_quarter", True),
             ("1/8", 0.5, "g_eighth", False), ("1/8T", 1.0/3, "g_eighth", True),
             ("1/16", 0.25, "g_sixteenth", False), ("1/16T", 1.0/6, "g_sixteenth", True),
             ("1/32", 0.125, "g_thirtysecond", False), ("1/32T", 1.0/12, "g_thirtysecond", True))

    def __init__(self, song, visible, session_visible=lambda: False):
        self.song, self.visible = song, visible
        self.session_visible = session_visible
        self.enabled = False
        self.grid_enabled = False
        self.grid_index = 4
        self.clip = None
        self.track = None
        self.slot = None
        self.pitch = 36
        self.page = 0
        self._notes = ()
        self._next_read = 0

    def sync(self, now=0):
        clip = target_clip(self.song) if self.visible() else None
        track = self.song.view.selected_track
        slot = None
        if self.session_visible() and getattr(track, "has_midi_input", False):
            candidate = getattr(self.song.view, "highlighted_clip_slot", None)
            if (candidate is None or getattr(candidate, "canonical_parent", None) != track) and clip is None:
                # Selecting a track header need not move Live's highlighted slot.
                # Use this track's cell in the selected Session scene instead.
                scenes = tuple(getattr(self.song, "scenes", ()))
                scene = getattr(self.song.view, "selected_scene", None)
                slots = tuple(getattr(track, "clip_slots", ()))
                if scene in scenes:
                    index = scenes.index(scene)
                    candidate = slots[index] if index < len(slots) else None
            if candidate is not None and getattr(candidate, "canonical_parent", None) == track:
                if not candidate.has_clip:
                    slot, clip = candidate, None
                elif candidate == self.slot and candidate.clip == self.clip:
                    slot, clip = candidate, candidate.clip
        if track != self.track or clip != self.clip or slot != self.slot:
            self.enabled = False
            self.grid_enabled = False
            self.clip, self.track, self.slot, self.page = clip, track, slot, 0
            self._next_read = 0
        self._read_grid()
        if clip is None and slot is None:
            self.grid_enabled = False
        if clip is not None:
            self.page = min(self.page, self.pages() - 1)
            if self.enabled and (now == 0 or now >= self._next_read):
                self._notes = tuple(all_notes(clip))
                self._next_read = now + 0.2
        else:
            self._notes = ()

    def _set_step(self, value):
        if abs(self.STEP - value) < 1e-10:
            return
        anchor = self.page * 16 * self.STEP
        self.STEP = value
        self.page = min(self.pages() - 1, int((anchor + 1e-7) / (16 * value)))
        self._next_read = 0

    def _read_grid(self):
        if self.clip is not None and hasattr(getattr(self.clip, "view", None), "grid_quantization"):
            value = grid_step(self.clip)
            if value is not None:
                for index, (_, beats, _, _) in enumerate(self.GRIDS):
                    if abs(beats - value) < 1e-7:
                        self.grid_index = index
                        break
                self._set_step(value)

    def _write_grid(self):
        if self.clip is not None and hasattr(getattr(self.clip, "view", None), "grid_quantization"):
            from Live.Clip import GridQuantization
            _, _, name, triplet = self.GRIDS[self.grid_index]
            self.clip.view.grid_quantization = getattr(GridQuantization, name)
            self.clip.view.grid_is_triplet = triplet

    def grid_toggle(self):
        self.sync()
        self.grid_enabled = not self.grid_enabled if self.clip is not None or self.slot is not None else False

    def grid_hold(self, held):
        self.sync()
        self.grid_enabled = bool(held and (self.clip is not None or self.slot is not None))

    def grid_turn(self, delta):
        self.sync()
        if self.grid_enabled:
            self.grid_index = max(0, min(len(self.GRIDS) - 1, self.grid_index + delta))
            self._set_step(self.GRIDS[self.grid_index][1])
            self._write_grid()

    def grid_packet(self, epoch):
        available = self.clip is not None or self.slot is not None
        label = self.GRIDS[self.grid_index][0]
        if abs(self.GRIDS[self.grid_index][1] - self.STEP) > 1e-7:
            label = "Custom"
        return "MG1 {} {} {} {} ready".format(epoch, int(available), int(self.grid_enabled), label)

    def bounds(self):
        if self.clip is None:
            length = float(getattr(self.song, "signature_numerator", 4)) * 4 / float(getattr(self.song, "signature_denominator", 4))
            return (0.0, length) if self.slot is not None else (0.0, 0.0)
        return (float(self.clip.loop_start), float(self.clip.loop_end)) if self.clip.looping else (float(self.clip.start_marker), float(self.clip.end_marker))

    def pages(self):
        start, end = self.bounds()
        return max(1, int(math.ceil((end - start) / (16 * self.STEP))))

    def toggle(self, pitch):
        self.sync()
        if (self.clip is not None or self.slot is not None) and self.bounds()[1] > self.bounds()[0] and 0 <= pitch <= 127:
            self.pitch = pitch
            self.enabled = not self.enabled
            self._next_read = 0
            self.sync()

    def select_pitch(self, pitch):
        self.sync()
        if self.enabled and 0 <= pitch <= 127:
            self.pitch = pitch
            self._next_read = 0

    def navigate(self, direction):
        if self.enabled:
            self.page = max(0, min(self.pages() - 1, self.page + direction))

    def hits(self, step):
        start, end = self.bounds()
        if end <= start:
            return []
        target = self.page * 16 + step
        last = max(0, int(math.ceil((end - start) / self.STEP)) - 1)
        hits = []
        for note in self._notes:
            if int(note.pitch) != self.pitch or not start <= note.start_time < end:
                continue
            # Associate a performed note with one nearest grid point, without
            # quantizing the clip. Midpoint ties belong to the later step.
            index = min(last, int(math.floor((note.start_time - start) / self.STEP + 0.5 + 1e-9)))
            if self.clip is not None and self.clip.looping:
                distance = abs(note.start_time - (start + index * self.STEP))
                if end - note.start_time <= distance + 1e-9:
                    index = 0
            if index == target:
                hits.append(note)
        return hits

    def pad(self, step):
        self.sync()
        if not self.enabled or not 0 <= step < 16:
            return
        start, end = self.bounds()
        at = start + (self.page * 16 + step) * self.STEP
        if at >= end - 1e-7:
            return
        notes = self.hits(step)
        self.song.begin_undo_step()
        try:
            created = self.clip is None
            if created:
                # Match Live's bundled SequencerClip: one bar, first step creates it.
                self.slot.create_clip(end - start)
                self.clip = self.slot.clip
                self.song.view.detail_clip = self.clip
                self._write_grid()
            if notes:
                self.clip.remove_notes_by_id(tuple(n.note_id for n in notes))
            else:
                from Live.Clip import MidiNoteSpecification
                note = MidiNoteSpecification(pitch=self.pitch, start_time=at,
                    duration=min(self.STEP, end - at), velocity=100, mute=False)
                self.clip.add_new_notes((note,))
            if created:
                from Live.Song import Quantization
                self.slot.fire(force_legato=True, launch_quantization=Quantization.q_no_q)
        finally:
            self.song.end_undo_step()
        self._next_read = 0
        self.sync()

    def duplicate(self):
        self.sync()
        if self.enabled and self.clip is not None and self.clip.looping:
            self.song.begin_undo_step()
            try:
                self.clip.duplicate_loop()
            finally:
                self.song.end_undo_step()
            self._next_read = 0

    def packet(self, epoch, now):
        self.sync(now)
        available = (self.clip is not None or self.slot is not None) and self.bounds()[1] > self.bounds()[0]
        valid = occupied = 0
        cursor = strip = -1
        strip_start = strip_end = 0
        color = 17
        if available:
            start, end = self.bounds()
            color = pad_color(getattr(self.clip if self.clip is not None else self.track, "color", 0xFFFFFF))
            for step in range(16):
                if start + (self.page * 16 + step) * self.STEP < end - 1e-7:
                    valid |= 1 << step
                if self.enabled and self.hits(step):
                    occupied |= 1 << step
            strip_start = min(24, int((self.page * 16 * self.STEP) / (end - start) * 25))
            strip_end = min(25, max(strip_start + 1, int(math.ceil(min(end - start, (self.page + 1) * 16 * self.STEP) / (end - start) * 25))))
            if self.enabled and self.clip is not None and self.song.is_playing and self.clip.is_playing:
                position = float(self.clip.playing_position)
                if start <= position < end:
                    strip = min(24, int((position - start) / (end - start) * 25))
                    current = int((position - start) / self.STEP) - self.page * 16
                    cursor = current if 0 <= current < 16 else -1
        return "MS1 {} {} {} {} {} {} {} {} {} {} {} {} {} {} ready".format(
            epoch, int(available), int(self.enabled), self.pitch, self.page, self.pages(), valid,
            occupied, cursor, color, strip_start, strip_end, strip, int(available and self.clip is not None and self.clip.looping))
