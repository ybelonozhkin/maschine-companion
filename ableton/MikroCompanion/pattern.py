from .model import pad_color


class PatternGrid:
    """Four visible tracks by four Session scenes, top row first in Live."""

    def __init__(self, song, highlight, show_clip=None):
        self.song = song
        self.highlight = highlight
        self.show_clip = show_clip
        self.enabled = False
        self.scene_mode = False
        self._positioned = False
        self.track = 0
        self.scene = 0
        self._held = {}
        self._frame = None

    def tracks(self):
        return tuple(getattr(self.song, "visible_tracks", self.song.tracks))

    def clamp(self):
        self.track = max(0, min(self.track, max(0, len(self.tracks()) - 4)))
        self.scene = max(0, min(self.scene, max(0, len(getattr(self.song, "scenes", ())) - 4)))

    def enter(self, scene_mode=False):
        self.enabled = True
        self.scene_mode = scene_mode
        tracks = self.tracks()
        selected = self.song.view.selected_track
        if not self._positioned and selected in tracks:
            self.track = tracks.index(selected) // 4 * 4
        scenes = tuple(getattr(self.song, "scenes", ()))
        selected = getattr(self.song.view, "selected_scene", None)
        if not self._positioned and selected in scenes:
            self.scene = scenes.index(selected)
        self._positioned = True
        self.clamp()
        self.update_highlight()

    def close(self):
        self.release_all()
        self.enabled = False
        self.scene_mode = False
        self.update_highlight()

    def release_all(self):
        slots, self._held = tuple(self._held.values()), {}
        for slot in slots:
            try:
                slot.set_fire_button_state(False)
            except (RuntimeError, ReferenceError, TypeError):
                pass

    def update_highlight(self):
        frame = (self.track, self.scene, 4, 4, False) if self.enabled else (-1, -1, -1, -1, False)
        if frame != self._frame:
            self.highlight(*frame)
            self._frame = frame

    def navigate(self, delta, horizontal=False):
        if not self.enabled:
            return
        self.release_all()
        if horizontal:
            self.track += delta * 4
        else:
            self.scene += delta
        self.clamp()
        self.update_highlight()

    def slot(self, position):
        tracks = self.tracks()
        column, row = position % 4, 3 - position // 4
        if not 0 <= position < 16 or self.track + column >= len(tracks):
            return None
        slots = getattr(tracks[self.track + column], "clip_slots", ())
        index = self.scene + row
        return slots[index] if index < len(slots) and index < len(self.song.scenes) else None

    def pad(self, value):
        if 48 <= value < 64:
            if self.enabled and not self.scene_mode:
                self.select(value - 48)
            return
        if 40 <= value < 44:
            if self.enabled and self.scene_mode:
                self.clamp()
                index = self.scene + value - 40
                scenes = tuple(getattr(self.song, "scenes", ()))
                if index < len(scenes):
                    scenes[index].fire()
            return
        if self.scene_mode:
            # Release any clip button held before switching from Pattern.
            if 16 <= value < 32:
                slot = self._held.pop(value - 16, None)
                if slot is not None:
                    slot.set_fire_button_state(False)
            return
        if 32 <= value < 36:
            if self.enabled:
                self.clamp()
                tracks = self.tracks()
                index = self.track + value - 32
                if index < len(tracks):
                    tracks[index].stop_all_clips()
            return
        position = value % 16
        if value >= 16:
            slot = self._held.pop(position, None)
            if slot is not None:
                try:
                    slot.set_fire_button_state(False)
                except (RuntimeError, ReferenceError, TypeError):
                    pass
            return
        if not self.enabled or not 0 <= value < 16 or position in self._held:
            return
        self.clamp()
        slot = self.slot(position)
        if slot is not None and getattr(slot, "has_clip", False):
            slot.set_fire_button_state(True)
            self._held[position] = slot
        elif slot is not None:
            self.select(position)

    def select(self, position):
        self.clamp()
        slot = self.slot(position)
        if slot is None:
            return
        track = self.tracks()[self.track + position % 4]
        scenes = tuple(self.song.scenes)
        row = self.scene + 3 - position // 4
        self.song.view.selected_track = track
        if row < len(scenes):
            self.song.view.selected_scene = scenes[row]
        self.song.view.highlighted_clip_slot = slot
        if slot.has_clip:
            self.song.view.detail_clip = slot.clip
            if self.show_clip is not None:
                self.show_clip()

    def packet(self, epoch):
        self.clamp()
        if self.enabled:
            self.update_highlight()
        colors, playing, triggered, recording, selected, selectable = [], 0, 0, 0, -1, 0
        highlighted = getattr(self.song.view, "highlighted_clip_slot", None)
        for position in range(16):
            slot = self.slot(position) if self.enabled and not self.scene_mode else None
            if slot is not None:
                selectable |= 1 << position
            clip = slot.clip if slot is not None and getattr(slot, "has_clip", False) else None
            picked = slot is not None and slot == highlighted
            if picked:
                selected = position
            colors.append(pad_color(clip.color) if clip is not None else 17 if picked else 0)
            if clip is not None:
                playing |= int(bool(clip.is_playing)) << position
                triggered |= int(bool(clip.is_triggered)) << position
                recording |= int(bool(clip.is_recording)) << position
        track_playing = 0
        if self.enabled and self.scene_mode:
            colors, playing, triggered, recording = [0] * 16, 0, 0, 0
            scenes = tuple(getattr(self.song, "scenes", ()))
            for row, scene in enumerate(scenes[self.scene:self.scene + 4]):
                position = (3 - row) * 4 + 3
                colors[position] = pad_color(getattr(scene, "color", 0xFFFFFF))
                triggered |= int(bool(getattr(scene, "is_triggered", False))) << position
                slots = getattr(scene, "clip_slots", ())
                if self.song.is_playing and any(getattr(slot, "has_clip", False) and slot.clip.is_playing for slot in slots):
                    playing |= 1 << position
        if self.enabled:
            for column, track in enumerate(self.tracks()[self.track:self.track + 4]):
                track_playing |= int(bool(self.song.is_playing) and getattr(track, "playing_slot_index", -1) >= 0) << column
        return "MPT5 {} {} {} {} {} {} {} {} {} {} {} {} {} {} ready".format(
            epoch, (2 if self.scene_mode else 1) if self.enabled else 0, self.track, self.scene,
            int(self.track > 0), int(self.track + 4 < len(self.tracks())),
            playing, triggered, recording, len(self.tracks()),
            " ".join(str(color) for color in colors), track_playing, selected, selectable)
