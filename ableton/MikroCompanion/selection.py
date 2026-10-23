from .editing import all_notes, target_clip


class NoteSelection:
    def __init__(self, song, visible):
        self.song, self.visible = song, visible
        self.enabled = False
        self.available = False
        self.clip = None
        self.track = None
        self.pitches = set()
        self.cursor = None
        self.anchor = None
        self._notes = []
        self._next_read = 0

    def sync(self, now=0):
        clip = target_clip(self.song) if self.visible() else None
        self.available = clip is not None
        if self.enabled and (clip is None or self.song.view.selected_track != self.track):
            self.enabled = False
        if clip != self.clip:
            self.clip = clip
            self.pitches.clear()
            self.cursor = self.anchor = None
            self._next_read = 0
        if self.enabled and (now == 0 or now >= self._next_read):
            self._next_read = now + 0.2
            self._notes = list(all_notes(clip))
            valid = {n.pitch for n in self._notes}
            self.pitches.intersection_update(valid)
            ids = {n.note_id for n in self._notes}
            if self.cursor not in ids:
                self.cursor = self.anchor = None
        return self.available

    def toggle(self):
        if self.enabled:
            self.enabled = False
            return
        if not self.sync():
            return
        self.enabled = True
        self.track = self.song.view.selected_track
        self.pitches = {n.pitch for n in self.clip.get_selected_notes_extended()}
        self.cursor = self.anchor = None
        self.sync()

    def _select(self, notes):
        self.clip.deselect_all_notes()
        ids = tuple(n.note_id for n in notes)
        if ids:
            self.clip.select_notes_by_id(ids)

    def pad(self, pitch):
        self.sync()
        if not self.enabled or not any(n.pitch == pitch for n in self._notes):
            return
        if pitch in self.pitches:
            self.pitches.remove(pitch)
        else:
            self.pitches.add(pitch)
        self.cursor = self.anchor = None
        self._select([n for n in self._notes if n.pitch in self.pitches])

    def turn(self, delta, extend=False):
        self.sync()
        if not self.enabled or not delta:
            return
        notes = sorted((n for n in self._notes if n.pitch in self.pitches),
                       key=lambda n: (n.start_time, n.pitch, n.note_id))
        if not notes:
            return
        ids = [n.note_id for n in notes]
        index = ids.index(self.cursor) if self.cursor in ids else None
        if extend:
            if self.anchor not in ids:
                self.anchor = self.cursor if index is not None else ids[0 if delta > 0 else -1]
            if index is None:
                index = ids.index(self.anchor)
            index = max(0, min(len(notes) - 1, index + delta))
            anchor = ids.index(self.anchor)
            chosen = notes[min(anchor, index):max(anchor, index) + 1]
        else:
            self.anchor = None
            index = (0 if delta > 0 else len(notes) - 1) if index is None else max(0, min(len(notes) - 1, index + delta))
            chosen = [notes[index]]
        self.cursor = ids[index]
        self._select(chosen)

    def erase(self):
        self.sync()
        if not self.enabled:
            return
        ids = tuple(n.note_id for n in self.clip.get_selected_notes_extended())
        if not ids:
            return
        self.song.begin_undo_step()
        try:
            self.clip.remove_notes_by_id(ids)
        finally:
            self.song.end_undo_step()
        self._next_read = 0
        self.sync()

    def can_erase(self):
        return self.enabled and bool(len(self.clip.get_selected_notes_extended()))

    def edited(self):
        self.sync()
        if self.enabled:
            self.pitches = {n.pitch for n in self.clip.get_selected_notes_extended()}

    def masks(self, pitches):
        present = {n.pitch for n in self._notes} if self.enabled else set()
        available = sum(1 << i for i, n in enumerate(pitches) if n >= 0 and n in present)
        selected = sum(1 << i for i, n in enumerate(pitches) if n >= 0 and n in self.pitches)
        return available, selected
