import math

SUPPORTED = (1, 2, 5, 6, 7, 8, 13, 14, 15, 16)
GRID_BEATS = {"g_8_bars": 32.0, "g_4_bars": 16.0, "g_2_bars": 8.0,
              "g_bar": 4.0, "g_half": 2.0, "g_quarter": 1.0,
              "g_eighth": 0.5, "g_sixteenth": 0.25,
              "g_thirtysecond": 0.125, "g_sixtyfourth": 0.0625}


def grid_step(clip):
    import Live
    grid = clip.view.grid_quantization
    for name, beats in GRID_BEATS.items():
        if grid == getattr(Live.Clip.GridQuantization, name, None):
            if name == "g_bar" or name.endswith("_bars"):
                beats *= float(getattr(clip, "signature_numerator", 4)) / float(getattr(clip, "signature_denominator", 4))
            return beats * (2.0 / 3.0 if clip.view.grid_is_triplet else 1.0)
    return None


def target_clip(song):
    clip = getattr(song.view, "detail_clip", None)
    if clip is None or not clip.is_midi_clip:
        return None
    owner = getattr(clip, "canonical_parent", None)
    for _ in range(4):
        if owner == song.view.selected_track:
            return clip
        if owner is None:
            break
        owner = getattr(owner, "canonical_parent", None)
    return None


def all_notes(clip):
    get_all = getattr(clip, "get_all_notes_extended", None)
    if get_all is not None:
        return get_all()
    start = min(0.0, float(clip.start_marker), float(clip.loop_start))
    end = max(float(clip.length), float(clip.end_marker), float(clip.loop_end))
    return clip.get_notes_extended(0, 128, start, end - start + 1.0)


def target_notes(clip):
    selected = clip.get_selected_notes_extended()
    return selected if len(selected) else all_notes(clip)


def changes(notes, command, step):
    if not len(notes):
        return []
    if command in (5, 6):
        if step is None:
            return []
        amount = 1.0 if command == 5 else 0.5
        return [(n, "start_time", n.start_time +
                 (math.floor(n.start_time / step + 0.5) * step - n.start_time) * amount) for n in notes]
    if command in (7, 8):
        if step is None:
            return []
        earliest = min(n.start_time for n in notes)
        delta = step / 2.0 * (-1 if command == 7 else 1)
        delta = max(delta, min(0.0, earliest) - earliest)
        return [(n, "start_time", n.start_time + delta) for n in notes]
    delta = {13: -1, 14: 1, 15: -12, 16: 12}.get(command)
    if delta is None or any(not 0 <= n.pitch + delta <= 127 for n in notes):
        return []
    return [(n, "pitch", n.pitch + delta) for n in notes]


def different(note, key, value):
    return not math.isclose(getattr(note, key), value, rel_tol=0, abs_tol=1e-9)


class Editor:
    def __init__(self, song, log):
        self.song, self.log = song, log
        self.shift = False
        self.mask = 0
        self._next_check = 0

    def availability(self, now):
        mask = int(self.song.can_undo) | (int(self.song.can_redo) << 1)
        if self.shift:
            if now >= self._next_check:
                self._next_check = now + 0.2
                self.mask = 0
                clip = target_clip(self.song)
                if clip is not None:
                    notes = target_notes(clip)
                    if len(notes):
                        step = grid_step(clip)
                        for command in SUPPORTED[2:]:
                            if any(different(n, key, value) for n, key, value in changes(notes, command, step)):
                                self.mask |= 1 << (command - 1)
            mask |= self.mask
        return mask

    def modifier(self, value):
        self.shift = bool(value)
        self._next_check = 0

    def execute(self, command):
        if command not in SUPPORTED:
            return False
        if command in (1, 2):
            if getattr(self.song, "can_undo" if command == 1 else "can_redo"):
                getattr(self.song, "undo" if command == 1 else "redo")()
                return True
            return False
        clip = target_clip(self.song)
        if clip is None:
            return False
        notes = target_notes(clip)
        modifications = [(n, key, value) for n, key, value in changes(notes, command, grid_step(clip))
                         if different(n, key, value)]
        if not modifications:
            return False
        self.song.begin_undo_step()
        try:
            for note, key, value in modifications:
                setattr(note, key, value)
            clip.apply_note_modifications(notes)
        finally:
            self.song.end_undo_step()
        self._next_check = 0
        return True
