#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 penguinwokrs
"""Generates the images the Stream Deck plugin ships with. Standard library only.

Stream Deck accepts SVG for action icons and key images, so those are written as markup and
stay sharp at every size the application asks for. The plugin's own icon is the one place
PNG is required, and it reuses the mark and the encoder from make-icon.py rather than
keeping a second copy of either.

The speaker and microphone are the same path data the tray draws in Icons.xaml, so a key on
the deck and a row in the tray's panel show the same shape. They are stroked rather than
filled: the tray learned that filling the speaker makes it read as much larger than the
icons beside it.

Two glyphs are deliberately not the tray's. The tray's battery row is drawn as a headset,
which is right beside a headset's own readings but on a deck is indistinguishable from this
plugin's own icon. And the tray's balance row is a game pad and a speech bubble side by
side, which at the twenty pixels an action list gives it collapses into a smudge - so the
deck gets the slider that its key face already draws.

Run after changing any glyph, then look at the result: an icon is only right if you have
seen it at the size it will be used.
"""
import importlib.util
from pathlib import Path

HERE = Path(__file__).parent
PLUGIN = HERE.parent / "plugin" / "com.penguinwokrs.openinzone.sdPlugin"

FOREGROUND = "#e8eaed"
MUTED = "#ff5c5c"
KEY_BACKGROUND = "#17171b"

# Authored on a 24x24 grid. Stroked unless the entry says otherwise.
SPEAKER = ["M4,9 L8,9 L13,4 L13,20 L8,15 L4,15 Z M16,9 A5,5 0 0 1 16,15"]
MIC = ["M12,3 A3,3 0 0 1 15,6 L15,12 A3,3 0 0 1 9,12 L9,6 A3,3 0 0 1 12,3 Z "
       "M6,11 A6,6 0 0 0 18,11 M12,17 L12,21"]
SLASH = ["M4,20 L20,4"]

# A battery, not the headset the tray uses: on a deck this sits next to the plugin's own
# headset icon, and two headsets side by side say nothing about which is which.
BATTERY = [
    "M3,8 L17,8 A2,2 0 0 1 19,10 L19,14 A2,2 0 0 1 17,16 L3,16 A1,1 0 0 1 2,15 "
    "L2,9 A1,1 0 0 1 3,8 Z",
    "M21,10.5 L21,13.5",
    "M5,10.5 L5,13.5 M8,10.5 L8,13.5",
]

# The key face draws the balance as a track with a marker on it; the icon says the same
# thing, which is what a game pad beside a speech bubble could not do at this size.
# No end stops: with them the track and its two caps read as a dumbbell rather than as
# something with a handle that slides along it.
BALANCE_TRACK = ["M3,12 L21,12"]
BALANCE_KNOB = ["M11.6,12 A3.2,3.2 0 1 0 18,12 A3.2,3.2 0 1 0 11.6,12 Z"]


def paths(figures, colour=FOREGROUND, filled=False):
    """One drawing instruction per figure, so a glyph can mix outlines and solid shapes."""
    return [(d, colour, filled) for d in figures]


def render(instructions):
    out = []
    for d, colour, filled in instructions:
        if filled:
            out.append(f'  <path d="{d}" fill="{colour}"/>')
        else:
            out.append(f'  <path d="{d}" fill="none" stroke="{colour}" stroke-width="1.8" '
                       f'stroke-linecap="round" stroke-linejoin="round"/>')
    return "\n".join(out)


def svg(instructions, size, background):
    """viewBox is always the 24x24 grid the glyphs are authored on; only the size changes."""
    frame = (f'  <rect width="24" height="24" rx="3" fill="{KEY_BACKGROUND}"/>\n'
             if background else "")
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" '
            f'width="{size}" height="{size}">\n{frame}{render(instructions)}\n</svg>\n')


ACTIONS = {
    "volume": paths(SPEAKER),
    "balance": paths(BALANCE_TRACK) + paths(BALANCE_KNOB, filled=True),
    "micmute": paths(MIC) + paths(SLASH, colour=MUTED),
    "miclevel": paths(MIC),
    "battery": paths(BATTERY),
}


def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    print(f"wrote {path.relative_to(PLUGIN.parent.parent)}")


def main():
    for name, instructions in ACTIONS.items():
        # 20px in the action list, 72px on a key. Both are the same markup at a different size.
        write(PLUGIN / "images" / "actions" / f"{name}.svg", svg(instructions, 24, background=False))
        write(PLUGIN / "images" / "keys" / f"{name}.svg", svg(instructions, 72, background=True))

    # The plugin's own icon is the one image Stream Deck requires as PNG.
    spec = importlib.util.spec_from_file_location("make_icon", HERE / "make-icon.py")
    make_icon = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(make_icon)

    for size, suffix in ((256, ""), (512, "@2x")):
        data = make_icon.png(size, make_icon.coverage(size))
        path = PLUGIN / "images" / f"plugin{suffix}.png"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        print(f"wrote {path.relative_to(PLUGIN.parent.parent)} ({size}x{size}, {len(data)} bytes)")


if __name__ == "__main__":
    main()
