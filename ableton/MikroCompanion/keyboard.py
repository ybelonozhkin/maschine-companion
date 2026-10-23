import base64


def scale_catalog():
    # This is also the source used by Live's bundled Launchkey MK4 script.
    import Live
    return tuple(Live.Song.get_all_scales_ordered())


def layout(start, root, intervals, enabled):
    allowed = set(int(n) % 12 for n in intervals) if enabled else set(range(12))
    notes = [n for n in range(max(0, start), 128) if (n - root) % 12 in allowed][:16]
    return tuple(notes + [-1] * (16 - len(notes)))


class Keyboard:
    def __init__(self):
        self.enabled = False
        self.starts = {}
        self._catalog = None

    @staticmethod
    def available(song):
        return all(hasattr(song, name) for name in ("root_note", "scale_name", "scale_intervals", "scale_mode"))

    def start(self, song, track):
        base = self.starts.get(track, 36)
        note = (base // 12) * 12 + int(song.root_note)
        return note if note <= 127 else note - 12

    def notes(self, song, track):
        return layout(self.start(song, track), int(song.root_note), song.scale_intervals, song.scale_mode)

    def turn(self, song, delta):
        if not self.enabled or not self.available(song):
            return
        if self._catalog is None:
            self._catalog = scale_catalog()
        names = [entry[0] for entry in self._catalog]
        if not names:
            return
        index = names.index(song.scale_name) if song.scale_name in names else 0
        song.scale_name = names[max(0, min(len(names) - 1, index + delta))]

    def adjust(self, song, delta, allow_disabled=False):
        if (not self.enabled and not allow_disabled) or not delta:
            return
        track = getattr(getattr(song, "view", None), "selected_track", None)
        note = max(0, min(127, self.start(song, track) + delta))
        self.starts[track] = note
        song.root_note = note % 12

    def packet(self, song, track, epoch):
        if not self.available(song):
            return None
        self.starts = {t: n for t, n in self.starts.items() if t in song.tracks}
        pitches = ",".join(str(note) for note in self.notes(song, track))
        name = base64.b64encode(song.scale_name.encode("utf-8")).decode("ascii")
        return "MK2 {} {} {} {} {} {} {} {} ready".format(
            epoch, int(self.enabled), int(song.scale_mode), int(song.root_note), self.start(song, track), pitches, name, 1)
