"""Choose the requested controller owner from public Live device metadata."""

MASCHINE_NAMES = frozenset(('maschine', 'maschine2', 'maschine3'))


def inspect_song(song):
    selected = song.view.selected_track
    count, selected_native, uncertain = 0, False, False
    seen = set()
    tracks = list(song.tracks) + list(getattr(song, 'return_tracks', ()))
    master = getattr(song, 'master_track', None)
    if master is not None:
        tracks.append(master)
    pending = [(track, track == selected) for track in tracks]
    while pending:
        parent, on_selected = pending.pop()
        try:
            for device in parent.devices:
                if device in seen:
                    continue
                seen.add(device)
                if len(seen) > 512:
                    return False, count, True
                original = str(getattr(device, 'class_display_name', '')).casefold().replace(' ', '')
                kind = str(getattr(device, 'class_name', ''))
                if original in MASCHINE_NAMES:
                    count += 1
                    selected_native = selected_native or on_selected
                elif kind == 'PluginDevice' and original in ('', 'plugindevice', 'plugin', 'plug-in'):
                    uncertain = True
                if getattr(device, 'can_have_chains', False):
                    pending.extend((chain, on_selected) for chain in device.chains)
                    pending.extend((chain, on_selected) for chain in getattr(device, 'return_chains', ()))
        except Exception:
            # Deleted/unknown devices must not authorize a hardware takeover.
            uncertain = True
    return selected_native, count, uncertain


def packet(song, epoch):
    selected, count, uncertain = inspect_song(song)
    return 'MO1 {} {} {} {} ready'.format(epoch, int(selected), count, int(uncertain))
