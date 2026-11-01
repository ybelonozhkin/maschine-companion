"""Publish Live state to the companion, preserving packet order and retries."""

import base64
import time

from .model import pad_color, numbered_track_name
from .ownership import inspect_song


def publish_state(surface):
    now = time.monotonic()
    if now < surface._retry_at:
        return
    try:
        if surface._layout_dirty:
            surface._rewire()
        surface._follow_recording_clip()
        song = surface.song()
        if now >= surface._ownership_at or song.view.selected_track != surface._ownership_track:
            surface._ownership_at = now + 0.5
            surface._ownership_track = song.view.selected_track
            ownership = inspect_song(song)
            if ownership != surface._ownership_state:
                surface.log_message('Mikro Companion NI ownership: selected={}, instances={}, uncertain={}'.format(*ownership))
            surface._ownership_state = ownership
        native, instances, uncertain = surface._ownership_state
        ownership_packet = 'MO1 {} {} {} {} ready'.format(surface._feedback_epoch, int(native), instances, int(uncertain))
        surface._socket.sendto(ownership_packet.encode('ascii'), ('127.0.0.1', 39473))
        sampled = time.perf_counter()
        packet = "MR2 {} {:.6f} {:.6f} {} {:.9f} ready".format(surface._feedback_epoch,
            float(getattr(song, "tempo", 120.0)), float(getattr(song, "current_song_time", 0.0)), int(song.is_playing), sampled)
        surface._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
        packet = surface._pattern.packet(surface._feedback_epoch)
        if surface._pattern.enabled:
            surface._socket.sendto(surface._pattern.edit_packet(surface._feedback_epoch).encode('ascii'), ('127.0.0.1', 39473))
        if packet != surface._last_pattern_packet or surface._state_dirty or now - surface._last_sent >= 0.5:
            surface._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
            surface._last_pattern_packet = packet
        if hasattr(surface.song().view, "detail_clip"):
            try:
                packet = surface._sequencer.packet(surface._feedback_epoch, now)
            except Exception as error:
                if surface._sequencer.enabled:
                    surface.log_message("Mikro Companion sequencer unavailable: {}".format(error))
                surface._sequencer.enabled = False
                packet = "MS1 {} 0 0 36 0 1 0 0 -1 17 0 0 -1 0 ready".format(surface._feedback_epoch)
            if not surface._sequencer.enabled and surface._step_return_grid is not None:
                surface._mixer.enabled = surface._mixer.tempo = surface._browser.enabled = False
                surface._pattern.enter(surface._step_return_grid)
                surface._step_return_grid = None
                pattern_packet = surface._pattern.packet(surface._feedback_epoch)
                surface._socket.sendto(pattern_packet.encode("ascii"), ("127.0.0.1", 39473))
                surface._last_pattern_packet = pattern_packet
                surface._state_dirty = True
            if packet != surface._last_step_packet or surface._state_dirty or now - surface._last_sent >= 0.5:
                surface._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
                surface._last_step_packet = packet
        if hasattr(surface.song().view, "detail_clip"):
            packet = surface._sequencer.grid_packet(surface._feedback_epoch)
            if packet != surface._last_grid_packet or surface._state_dirty or now - surface._last_sent >= 0.5:
                surface._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
                surface._last_grid_packet = packet
        if hasattr(surface.application().view, "show_view"):
            packet = "MB1 {} {} ready".format(surface._feedback_epoch, int(surface._browser.enabled))
            if packet != surface._last_browser_packet or surface._state_dirty or now - surface._last_sent >= 0.5:
                surface._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
                surface._last_browser_packet = packet
        if hasattr(surface.song(), "tempo"):
            packet = "MCU1 {} {} {:.2f} ready".format(surface._feedback_epoch, int(surface._mixer.tempo), surface.song().tempo)
            surface._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
        try:
            device_packet = surface._devices.packet(surface._feedback_epoch)
            surface._last_device_error = None
        except Exception as error:
            surface._devices.close()
            device_packet = "MDEV1 {} 0 0 0 0 0 0 - - - ready".format(surface._feedback_epoch)
            if str(error) != surface._last_device_error:
                surface.log_message("Mikro Companion device state unavailable: {}".format(error))
                surface._last_device_error = str(error)
        if device_packet != surface._last_device_packet or surface._state_dirty or now - surface._last_sent >= 0.5:
            surface._socket.sendto(device_packet.encode("ascii"), ("127.0.0.1", 39473))
            surface._last_device_packet = device_packet
        try:
            navigation_packet = surface._navigation.packet(surface._feedback_epoch)
            surface._last_navigation_error = None
        except Exception as error:
            surface._navigation.close()
            navigation_packet = "MNAV1 {} 0 0 - ready".format(surface._feedback_epoch)
            if str(error) != surface._last_navigation_error:
                surface.log_message("Mikro Companion navigation state unavailable: {}".format(error))
                surface._last_navigation_error = str(error)
        if navigation_packet != surface._last_navigation_packet or surface._state_dirty or now - surface._last_sent >= 0.5:
            surface._socket.sendto(navigation_packet.encode("ascii"), ("127.0.0.1", 39473))
            surface._last_navigation_packet = navigation_packet
        mixer_packet = surface._mixer.packet(surface._feedback_epoch)
        if mixer_packet is not None and (mixer_packet != surface._last_mixer_packet or surface._state_dirty or now - surface._last_sent >= 0.5):
            surface._socket.sendto(mixer_packet.encode("ascii"), ("127.0.0.1", 39473))
            surface._last_mixer_packet = mixer_packet
        if hasattr(surface.song().view, "detail_clip"):
            try:
                surface._selection.sync(now)
                pitches = surface._keyboard.notes(surface.song(), surface._track) if surface._keyboard.enabled else range(surface._base_note(), surface._base_note() + 16)
                present, selected = surface._selection.masks(pitches)
                selection = (surface._feedback_epoch, int(surface._selection.available), int(surface._selection.enabled), present, selected, int(surface._selection.can_erase()))
                surface._last_selection_error = None
            except Exception as error:
                surface._selection.enabled = False
                selection = (surface._feedback_epoch, 0, 0, 0, 0, 0)
                if str(error) != surface._last_selection_error:
                    surface.log_message("Mikro Companion selection state unavailable: {}".format(error))
                surface._last_selection_error = str(error)
            if selection != surface._last_selection_state or surface._state_dirty or now - surface._last_sent >= 0.5:
                packet = "MV2 {} {} {} {} {} {} ready".format(*selection)
                surface._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
                surface._last_selection_state = selection
        if hasattr(surface.song(), "can_undo"):
            try:
                edit_mask = surface._editor.availability(now)
                if surface._selection.enabled and surface._selection.can_erase():
                    edit_mask |= 1 << 8  # Shift + pad 9: Clear selected notes.
                history = (bool(surface.song().can_undo), bool(surface.song().can_redo))
                if surface._editor.shift and history != surface._last_history:
                    surface.log_message("Mikro Companion history state: undo={}; redo={}".format(*history))
                    surface._last_history = history
                if edit_mask != surface._last_edit_mask or surface._state_dirty or now - surface._last_sent >= 0.5:
                    packet = "ME1 {} {} ready".format(surface._feedback_epoch, edit_mask)
                    surface._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
                    surface._last_edit_mask = edit_mask
                surface._last_edit_error = None
            except Exception as error:
                message = str(error)
                if message != surface._last_edit_error:
                    surface.log_message("Mikro Companion edit state unavailable: {}".format(message))
                surface._last_edit_error = message
                packet = "ME1 {} {} ready".format(surface._feedback_epoch, int(surface.song().can_undo) | (int(surface.song().can_redo) << 1))
                surface._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
        if surface._state_dirty or now - surface._last_sent >= 0.5:
            song = surface.song()
            color = pad_color(getattr(surface._track, "color", 0x0000FF))
            base = surface._base_note()
            mask = surface._occupied_mask(base)
            mix = surface._mix_track()
            normal = surface._track in tuple(song.tracks)
            notes = normal and getattr(surface._track, "has_midi_input", True)
            packet = "MN1 {} {} {} ready".format(surface._feedback_epoch, int(notes), int(normal))
            surface._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
            state = "MC8 {} {} {} {} {} {} {} {} {} {} {} {} {} {} {} ready".format(
                mask, int(song.is_playing), int(surface._recording_active()), color, base, surface._feedback_epoch, int(song.metronome), int(song.view.follow_song),
                surface._selected_note(), int(getattr(mix, "mute", False)) if mix is not None else 0, int(getattr(mix, "solo", False)) if mix is not None else 0, int(mix is not None),
                int(native), instances, int(uncertain)
            )
            if hasattr(surface._track, "name"):
                name = ''.join(c for c in surface._track.name if c.isprintable())[:64] or 'Track'
                tracks = tuple(song.tracks)
                returns = tuple(getattr(song, 'return_tracks', ()))
                if surface._track in tracks:
                    name = numbered_track_name(name, str(tracks.index(surface._track) + 1))
                elif surface._track in returns:
                    index = returns.index(surface._track)
                    name = numbered_track_name(name, chr(65 + index) if index < 26 else 'R{}'.format(index + 1))
                encoded = base64.b64encode(name.encode('utf-8')).decode('ascii')
                packet = "MD1 {} {} ready".format(surface._feedback_epoch, encoded)
                surface._socket.sendto(packet.encode('ascii'), ('127.0.0.1', 39473))
            keyboard = surface._keyboard.packet(song, surface._track, surface._feedback_epoch)
            if keyboard is not None:
                surface._socket.sendto(keyboard.encode("ascii"), ("127.0.0.1", 39473))
            if hasattr(song, "loop"):
                packet = "MT1 {} {} ready".format(surface._feedback_epoch, int(song.loop))
                surface._socket.sendto(packet.encode("ascii"), ("127.0.0.1", 39473))
            surface._socket.sendto(state.encode("ascii"), ("127.0.0.1", 39473))
            surface._last_sent = now
            surface._state_dirty = False
            layout = (mask, color, base)
            if layout != surface._last_layout:
                notes = [base + i for i in range(16) if mask & (1 << i)]
                surface.log_message("Mikro Companion notes: {}; pad color: {}".format(notes, color))
                surface._last_layout = layout
        if surface._keyboard.enabled:
            notes = [n for n in surface._keyboard.notes(surface.song(), surface._track) if n >= 0]
            if notes:
                surface._clip_activity.update(surface.song(), surface._track, min(notes), max(notes) - min(notes) + 1)
        else:
            surface._clip_activity.update(surface.song(), surface._track, surface._base_note())
        if surface._last_error is not None:
            surface.log_message("Mikro Companion state recovered")
        surface._last_error = None
    except Exception as error:
        # No heartbeat on a failed state read: the companion releases notes.
        message = "{}: {}".format(type(error).__name__, error)
        if message != surface._last_error:
            surface.log_message("Mikro Companion state unavailable: {}".format(message))
        surface._last_error = message
        surface._retry_at = now + 1.0
        surface._state_dirty = True
        surface._layout_dirty = True
