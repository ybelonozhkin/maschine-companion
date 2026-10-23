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
        parameter = self.parameter()
        if self.enabled and parameter is not None:
            step = (float(parameter.max) - float(parameter.min)) / 200.0
            parameter.value = max(parameter.min, min(parameter.max, parameter.value + delta * step))

    @staticmethod
    def decibels(text):
        text = text.replace("\u2212", "-").replace("\u221e", "inf")
        match = re.search(r"[-+]?(?:\d+(?:\.\d*)?|inf)", text, re.IGNORECASE)
        return float(match.group()) if match else None

    def set_db(self, target):
        parameter = self.parameter()
        if not self.enabled or parameter is None:
            return
        low, high = float(parameter.min), float(parameter.max)
        for _ in range(32):
            middle = (low + high) / 2
            value = self.decibels(parameter.str_for_value(middle))
            if value is None:
                return
            if value < target:
                low = middle
            else:
                high = middle
        parameter.value = (low + high) / 2

    def reset(self):
        parameter = self.parameter()
        if self.enabled and parameter is not None:
            default = getattr(parameter, "default_value", None)
            if default is not None and self.decibels(parameter.str_for_value(default)) == 0:
                parameter.value = default
            else:
                self.set_db(0.0)

    def coarse(self, delta):
        parameter = self.parameter()
        if self.enabled and parameter is not None and delta:
            current = self.decibels(str(parameter))
            if current is not None:
                target = (round(current) + delta) if math.isfinite(current) else -70.0
                self.set_db(target)

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
