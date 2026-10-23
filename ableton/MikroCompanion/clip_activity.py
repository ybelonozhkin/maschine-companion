import time


class ClipActivity:
    """Visualize clip note starts without changing a Set or its MIDI routing."""

    def __init__(self, pulse, log, clock=time.monotonic):
        self._pulse, self._log, self._clock = pulse, log, clock
        self._clip = None
        self._subscriptions = []
        self._notes = []
        self._dirty = True
        self._base = None
        self._position = None
        self._wall = None
        self._last_error = None

    def _changed(self):
        self._dirty = True

    def disconnect(self):
        subscriptions, self._subscriptions = self._subscriptions, []
        for obj, name, callback in subscriptions:
            try:
                getattr(obj, "remove_" + name + "_listener")(callback)
            except Exception:
                pass
        self._clip = None
        self._position = None
        self._wall = None
        self._notes = []

    def _bind(self, clip):
        self.disconnect()
        self._clip = clip
        self._dirty = True
        if clip is not None:
            for name in ["notes", "loop_start", "loop_end", "looping"]:
                add = getattr(clip, "add_" + name + "_listener", None)
                if add is not None:
                    add(self._changed)
                    self._subscriptions.append((clip, name, self._changed))

    def _playing_clip(self, track):
        index = int(getattr(track, "playing_slot_index", -1))
        if index >= 0:
            slots = track.clip_slots
            return slots[index].clip if index < len(slots) and slots[index].has_clip else None
        if getattr(track, "back_to_arranger", False):
            return None
        for clip in getattr(track, "arrangement_clips", []):
            if clip.is_playing and clip.is_midi_clip:
                return clip
        return None

    def _read_notes(self, clip, base, count=16):
        start = min(0.0, float(clip.loop_start), float(getattr(clip, "start_marker", 0.0)))
        end = max(float(clip.length), float(clip.loop_end), float(getattr(clip, "end_marker", 0.0)))
        extended = getattr(clip, "get_notes_extended", None)
        notes = []
        if extended is not None:
            raw = extended(base, count, start, end - start + 1.0)
            if isinstance(raw, dict):
                raw = raw["notes"]
            for note in raw:
                def get(key, default=None):
                    return note.get(key, default) if isinstance(note, dict) else getattr(note, key, default)
                if not get("mute", False) and get("velocity", 0) > 0 and get("probability", 1.0) > 0:
                    notes.append((float(get("start_time")), int(get("pitch")), float(get("duration"))))
        else:
            for pitch, onset, duration, velocity, mute in clip.get_notes(start, base, end - start + 1.0, count):
                if velocity > 0 and not mute:
                    notes.append((float(onset), int(pitch), float(duration)))
        self._notes = sorted(notes)
        self._dirty = False

    def update(self, song, track, base, count=16):
        try:
            self._update(song, track, base, count)
            self._last_error = None
        except Exception as error:
            message = str(error)
            if message != self._last_error:
                self._log("Mikro Companion clip activity unavailable: " + message)
            self._last_error = message
            self._position = None
            self._dirty = True

    def _update(self, song, track, base, count):
        if not song.is_playing or getattr(track, "mute", False) or getattr(track, "muted_via_solo", False):
            if self._clip is not None:
                self._bind(None)
            return
        clip = self._playing_clip(track)
        if clip is None or not clip.is_midi_clip or not clip.is_playing or getattr(clip, "muted", False):
            if self._clip is not None:
                self._bind(None)
            return
        if clip != self._clip:
            self._bind(clip)
        if (base, count) != self._base:
            self._base, self._dirty, self._position = (base, count), True, None
        if self._dirty:
            self._read_notes(clip, base, count)
        position, now = float(clip.playing_position), self._clock()
        previous = self._position
        distance = position - previous if previous is not None else 0.0
        wrapped = previous is not None and position < previous and clip.looping
        if wrapped:
            distance += float(clip.loop_end) - float(clip.loop_start)
        if previous is not None:
            expected = max(0.5, (now - self._wall) * float(song.tempo) / 60.0 * 2.0 + 0.25)
            if distance < 0 or distance > expected:
                previous = None
        pitches = set()
        for onset, pitch, duration in self._notes:
            if not base <= pitch < base + count:
                continue
            if previous is None:
                triggered = onset <= position < onset + max(duration, 0.001)
            elif wrapped:
                triggered = previous < onset < float(clip.loop_end) or float(clip.loop_start) <= onset <= position
            else:
                triggered = previous < onset <= position
            if triggered:
                pitches.add(pitch)
        self._position, self._wall = position, now
        for pitch in pitches:
            self._pulse(pitch)
