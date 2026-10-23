import colorsys


def numbered_track_name(name, prefix):
    """Live's default names already contain their track number."""
    if any(name.startswith(prefix + separator) for separator in ('-', ' ', ':', '.')) or name == prefix:
        return name
    return '{} {}'.format(prefix, name)


def next_pad_bank(base, delta):
    # The end banks are clipped; they must not become new alignment anchors.
    banks = (0, 4, 20, 36, 52, 68, 84, 100, 112)
    for _ in range(abs(delta)):
        choices = [n for n in banks if n > base] if delta > 0 else [n for n in banks if n < base]
        if choices:
            base = min(choices) if delta > 0 else max(choices)
    return base


def pad_color(rgb):
    """Approximate Live RGB with the Mikro's indexed hardware palette."""
    red, green, blue = ((int(rgb) >> shift) & 255 for shift in (16, 8, 0))
    hue, saturation, _ = colorsys.rgb_to_hsv(red / 255.0, green / 255.0, blue / 255.0)
    if saturation < 0.15:
        return 17
    hues = (0, 20, 35, 45, 60, 90, 120, 150, 180, 210, 240, 255, 270, 285, 300, 330)
    degrees = hue * 360
    return 1 + min(range(len(hues)), key=lambda i: abs((degrees - hues[i] + 180) % 360 - 180))
