class SamplerActivity:
    """Observe actual Simpler playback without changing device settings."""

    def __init__(self, listen, pulse, error):
        self._listen = listen
        self._pulse = pulse
        self._error = error
        self.sources = 0

    def watch(self, device, note):
        state = {"enabled": False, "position": 0.0, "error": None}

        def changed():
            try:
                enabled = bool(device.playing_position_enabled)
                position = float(device.playing_position)
                trigger = enabled and (not state["enabled"] or position < state["position"] - 0.01)
                state["enabled"], state["position"] = enabled, position
                if trigger:
                    self._pulse(note)
                state["error"] = None
            except Exception as error:
                message = str(error)
                if message != state["error"]:
                    self._error(message)
                state["error"] = message

        self._listen(device, "playing_position", changed)
        self._listen(device, "playing_position_enabled", changed)
        self.sources += 1
        changed()
