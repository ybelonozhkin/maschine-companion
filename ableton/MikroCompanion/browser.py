class Browser:
    def __init__(self, application, send_key=None):
        self.application = application
        self.enabled = False
        self._send_key = send_key

    def toggle(self):
        self.enabled = not self.enabled
        if self.enabled:
            self.application.view.show_view("Browser")
            self.application.view.focus_view("Browser")
            if self._send_key is not None:
                self._send_key(256)

    def close(self):
        self.enabled = False

    def press(self, key):
        if self.enabled and self._send_key is not None and key in (13, 37, 38, 39, 40, 49):
            self._send_key(key)

    def turn(self, delta):
        if abs(delta) >= 6:
            if self.enabled and self._send_key is not None:
                self._send_key(1024 + max(-63, min(63, delta)))
            return
        for _ in range(min(16, abs(delta))):
            self.press(40 if delta > 0 else 38)
