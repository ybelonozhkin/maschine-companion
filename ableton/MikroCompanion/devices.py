import base64
import math


class DeviceControl:
    """Selected-device parameters; navigation never changes a parameter value."""

    def __init__(self, song, show_devices):
        self.song = song
        self.show_devices = show_devices
        self.enabled = False
        self.track = self.device = self.parameter = None
        self._parameters = ()
        self._devices = ()
        self._positions = {}

    def sync(self):
        track = self.song.view.selected_track
        if track != self.track:
            self.track = track
            self._positions.clear()
        devices = tuple(getattr(track, "devices", ()))
        selected = getattr(getattr(track, "view", None), "selected_device", None)
        # A device chosen inside a Rack follows that chain, without flattening
        # unrelated chains or changing the sound selected for playing.
        if selected is not None and selected not in devices:
            parent = getattr(selected, "canonical_parent", None)
            siblings = tuple(getattr(parent, "devices", ()))
            if selected in siblings:
                devices = siblings
        self._devices = devices
        device = selected if selected in devices else (devices[0] if devices else None)
        if device is None:
            self.enabled = False
        parameters = tuple(p for p in getattr(device, "parameters", ())
                           if getattr(p, "is_enabled", True))
        if device != self.device:
            self.device = device
            self.parameter = self._positions.get(device)
        self._parameters = parameters
        if self.parameter not in parameters:
            self.parameter = parameters[0] if parameters else None
        if device is not None:
            self._positions[device] = self.parameter
        # Discard references to devices removed from the selected chain.
        self._positions = {d: p for d, p in self._positions.items() if d in devices}

    def toggle(self):
        if self.enabled:
            self.enabled = False
            return
        self.sync()
        if self.device is not None:
            self.enabled = True
            self.song.view.select_device(self.device)
            self.show_devices()

    def close(self):
        self.enabled = False

    def neighbors(self, devices=False):
        items, selected = ((self._devices, self.device) if devices
                           else (self._parameters, self.parameter))
        if selected not in items:
            return False, False
        index = items.index(selected)
        return index > 0, index + 1 < len(items)

    def navigate(self, delta, devices=False):
        if not self.enabled or not delta:
            return
        self.sync()
        items, selected = ((self._devices, self.device) if devices
                           else (self._parameters, self.parameter))
        if selected not in items:
            return
        index = max(0, min(len(items) - 1, items.index(selected) + delta))
        if devices:
            self.song.view.select_device(items[index])
            self.sync()
            self.show_devices()
        else:
            self.parameter = items[index]
            self._positions[self.device] = self.parameter

    def turn(self, delta, fast=False):
        if not self.enabled or not delta:
            return
        self.sync()
        p = self.parameter
        if p is None:
            return
        low, high, current = float(p.min), float(p.max), float(p.value)
        if not all(math.isfinite(v) for v in (low, high, current)) or high <= low:
            return
        delta *= 5 if fast else 1
        if getattr(p, "is_quantized", False):
            value = round(current) + delta
        else:
            value = current + delta * (high - low) / 127
        p.value = max(low, min(high, value))

    def reset(self):
        if not self.enabled:
            return
        self.sync()
        p = self.parameter
        if p is None or getattr(p, "is_quantized", False):
            return
        try:
            default = getattr(p, "default_value", None)
        except (AttributeError, RuntimeError):
            return
        if default is not None:
            low, high, value = float(p.min), float(p.max), float(default)
            if all(math.isfinite(v) for v in (low, high, value)) and high >= low:
                p.value = max(low, min(high, value))

    def strip(self, position):
        if not self.enabled or not 0 <= position <= 127:
            return
        self.sync()
        p = self.parameter
        if p is None:
            return
        low, high = float(p.min), float(p.max)
        if not all(math.isfinite(v) for v in (low, high)) or high <= low:
            return
        value = low + position * (high - low) / 127
        if getattr(p, "is_quantized", False):
            value = round(value)
        p.value = max(low, min(high, value))

    def strip_position(self):
        p = self.parameter
        if p is None:
            return -1
        low, high, value = float(p.min), float(p.max), float(p.value)
        if not all(math.isfinite(v) for v in (low, high, value)) or high <= low:
            return -1
        return max(0, min(127, round((value - low) * 127 / (high - low))))

    @staticmethod
    def _text(value, limit):
        text = "".join(c if ord(c) >= 32 else " " for c in str(value))[:limit]
        return base64.b64encode(text.encode("utf-8")[:96].decode("utf-8", "ignore").encode("utf-8")).decode("ascii") or "-"

    def packet(self, epoch):
        if not self.enabled:
            available = bool(getattr(self.song.view.selected_track, "devices", ()))
            return "MDEV2 {} {} 0 0 0 0 0 - - - -1 ready".format(epoch, int(available))
        self.sync()
        left, right = self.neighbors()
        previous, following = self.neighbors(True)
        device = getattr(self.device, "name", "No devices")
        parameter = getattr(self.parameter, "name", "No parameters")
        value = str(self.parameter) if self.parameter is not None else ""
        return "MDEV2 {} {} {} {} {} {} {} {} {} {} {} ready".format(
            epoch, int(self.device is not None), int(self.enabled),
            int(left), int(right), int(previous), int(following),
            self._text(device, 40), self._text(parameter, 40), self._text(value, 32), self.strip_position())
