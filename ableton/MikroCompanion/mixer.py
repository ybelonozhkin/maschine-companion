import base64
import math
import re


class Mixer:
    def __init__(self, song):
        self.song = song
        self.enabled = False
        self.tempo = False

    def tracks(self):
        tracks = list(getattr(self.song, "visible_tracks", self.song.tracks))
        for track in tuple(getattr(self.song, "return_tracks", ())) + (getattr(self.song, "master_track", None),):
            if track is not None and track not in tracks:
                tracks.append(track)
        return tracks

    def neighbors(self):
        tracks = self.tracks()
        selected = self.song.view.selected_track
        if selected not in tracks:
            return False, False
        index = tracks.index(selected)
        return index > 0, index < len(tracks) - 1

    def navigate(self, direction):
        tracks = self.tracks()
        selected = self.song.view.selected_track
        if selected in tracks:
            index = max(0, min(len(tracks) - 1, tracks.index(selected) + direction))
            self.song.view.selected_track = tracks[index]

    def parameter(self):
        mixer = getattr(self.song.view.selected_track, "mixer_device", None)
        parameter = getattr(mixer, "volume", None)
        return parameter if parameter is not None and getattr(parameter, "is_enabled", True) else None

    def toggle(self):
        self.tempo = False
        self.enabled = not self.enabled if self.parameter() is not None else False

    def turn(self, delta):
        self.adjust_db(delta, 0.5)

    @staticmethod
    def decibels(text):
        text = text.replace("\u2212", "-").replace("\u221e", "inf")
        match = re.search(r"[-+]?(?:\d+(?:\.\d*)?|inf)", text, re.IGNORECASE)
        return float(match.group()) if match else None

    def set_db(self, target):
        parameter = self.parameter()
        if not self.enabled or parameter is None:
            return
        default = getattr(parameter, "default_value", None)
        if target == 0 and default is not None and self.decibels(parameter.str_for_value(default)) == 0:
            parameter.value = default
            return
        if target == 0:
            low, high = float(parameter.min), float(parameter.max)
            for _ in range(32):
                middle = (low + high) / 2
                value = self.decibels(parameter.str_for_value(middle))
                if value is None:
                    return
                # Preserve the sign of formatted -0.00 to locate unity.
                if value < 0 or (value == 0 and math.copysign(1, value) < 0):
                    low = middle
                else:
                    high = middle
            parameter.value = high
            return
        # Live formats dB with limited precision. Find both ends of the
        # matching interval and use its center, rather than its rounded edge.
        boundaries = []
        for upper in (False, True):
            low, high = float(parameter.min), float(parameter.max)
            for _ in range(32):
                middle = (low + high) / 2
                value = self.decibels(parameter.str_for_value(middle))
                if value is None:
                    return
                if value < target or (upper and value == target):
                    low = middle
                else:
                    high = middle
            boundaries.append((low + high) / 2)
        parameter.value = sum(boundaries) / 2

    def reset(self):
        parameter = self.parameter()
        if self.enabled and parameter is not None:
            default = getattr(parameter, "default_value", None)
            if default is not None and self.decibels(parameter.str_for_value(default)) == 0:
                parameter.value = default
            else:
                self.set_db(0.0)

    def adjust_db(self, delta, step):
        parameter = self.parameter()
        if self.enabled and parameter is not None and delta:
            current = self.decibels(str(parameter))
            if current is not None:
                if not math.isfinite(current) and delta < 0:
                    return
                target = round((math.floor(current / step + 0.5) + delta) * step, 1) if math.isfinite(current) else -70.0
                self.set_db(target)

    def fine(self, delta):
        self.adjust_db(delta, 0.1)

    def toggle_tempo(self):
        self.tempo = not self.tempo
        self.enabled = False

    def tempo_turn(self, delta, coarse=False):
        if self.tempo:
            self.song.tempo = max(20.0, min(999.0, math.floor(self.song.tempo + 0.5) + delta))

    def packet(self, epoch):
        if not hasattr(self.song.view.selected_track, "mixer_device"):
            return None
        parameter = self.parameter()
        if parameter is None:
            self.enabled = False
        left, right = self.neighbors()
        text = str(parameter) if parameter is not None else "--"
        text = text.replace("\u221e", "INF").replace("\u2212", "-").replace("\n", " ").replace("\r", " ")
        encoded = base64.b64encode(text.encode("utf-8")).decode("ascii") or "LQ=="
        return "MX1 {} {} {} {} {} {} ready".format(epoch, int(parameter is not None),
                    int(self.enabled), int(left), int(right), encoded)
