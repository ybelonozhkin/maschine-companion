import base64


class Navigation:
    """Arrangement navigation independent of the visible document view."""

    def __init__(self, song):
        self.song = song
        self.enabled = False
        self.page = 0
        self._pending_start = None
        self._marker = None

    def toggle(self):
        self.enabled = not self.enabled

    def close(self):
        self.enabled = False

    def navigate(self, direction):
        if self.enabled:
            self.page = max(0, min(1, self.page + direction))

    def _remember_start(self):
        if not self.song.is_playing:
            position = float(self.song.current_song_time)
            self.song.start_time = position
            self._pending_start = position

    def turn(self, delta, fast=False):
        if not self.enabled or not delta:
            return
        song = self.song
        if self.page == 0:
            bar = float(song.signature_numerator) * 4 / song.signature_denominator
            song.current_song_time = max(0.0, song.current_song_time + delta * bar * (4 if fast else 1))
            self._marker = None
        else:
            cues = sorted(song.cue_points, key=lambda cue: cue.time)
            if not cues:
                self._marker = None
                return
            # Retain the chosen locator while a quantized jump is pending.
            origin = self._marker.time if self._marker in cues else song.current_song_time
            for _ in range(abs(delta)):
                candidates = [cue for cue in cues if cue.time > origin + 1e-6] if delta > 0 else [cue for cue in cues if cue.time < origin - 1e-6]
                if not candidates:
                    break
                self._marker = candidates[0] if delta > 0 else candidates[-1]
                if song.is_playing:
                    self._marker.jump()
                else:
                    song.current_song_time = self._marker.time
                origin = self._marker.time
            # While stopped, place the next start at the locator itself.
            if not song.is_playing and self._marker in cues:
                song.current_song_time = self._marker.time
        self._remember_start()

    def play(self):
        position = self._pending_start
        self._pending_start = None
        if position is not None and not self.song.is_playing and abs(self.song.start_time - position) < 1e-6 and abs(self.song.current_song_time - position) < 1e-6:
            self.song.continue_playing()
        else:
            self.song.start_playing()

    def cancel_start(self):
        self._pending_start = self._marker = None

    def packet(self, epoch):
        label = ""
        if self.enabled:
            if self.page == 0:
                label = ".".join(str(self.song.get_current_beats_song_time()).split(".")[:3])
            else:
                cues = tuple(self.song.cue_points)
                if not cues:
                    self._marker = None
                    label = "No markers"
                else:
                    marker = self._marker
                    if marker not in cues:
                        before = [cue for cue in cues if cue.time <= self.song.current_song_time + 1e-6]
                        marker = max(before, key=lambda cue: cue.time) if before else min(cues, key=lambda cue: cue.time)
                    label = marker.name or "Unnamed"
        label = "".join(char for char in label if ord(char) >= 32)[:60]
        encoded = base64.b64encode(label.encode("utf-8")[:180].decode("utf-8", "ignore").encode("utf-8")).decode("ascii") or "-"
        return "MNAV1 {} {} {} {} ready".format(epoch, int(self.enabled), self.page, encoded)
