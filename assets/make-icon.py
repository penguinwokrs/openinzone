#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 penguinwokrs
"""Generates assets/openinzone.ico. Standard library only, so it runs anywhere.

The mark is a headband arc with an earcup on each side.

Two things here are not obvious and both were learned the hard way:

Sizes below 256 are written as classic DIB bitmaps, not PNG. Windows only reliably
decodes PNG-compressed ICO entries at 256x256; at the small sizes the shell uses for
the taskbar, Alt+Tab and the startup-apps list, a PNG entry is silently skipped and the
application shows no icon at all. An earlier version wrote every size as PNG and that is
exactly what happened.

Everything is drawn at four times the target and averaged down, because a hard
inside/outside test at 16 pixels produces a jagged, barely legible mark.
"""
import math
import struct
import zlib
from pathlib import Path

SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)
SUPERSAMPLE = 4
FOREGROUND = (0xE8, 0xEA, 0xED)


def coverage(size):
    """Returns a size x size grid of 0..1 coverage, supersampled for smooth edges."""
    n = size * SUPERSAMPLE
    cx = n / 2

    # Proportions chosen so the mark fills the canvas with an even margin, and so the earcups
    # are unmistakably fatter than the band rather than reading as thickened ends of it. The
    # band's sides run a little past its centre line so they meet the cups with no seam.
    band_r = n * 0.30                   # radius to the middle of the headband
    band_t = max(1.0, n * 0.09)         # how thick the headband is
    band_cy = n * 0.425                 # centre of the arc
    band_drop = n * 0.10                # how far the sides continue below that centre
    cup_w, cup_h = n * 0.18, n * 0.40
    cup_cy = n * 0.72
    cup_r = min(cup_w, cup_h) * 0.45    # rounded corners on the earcups

    hits = [[0] * n for _ in range(n)]
    for y in range(n):
        for x in range(n):
            dx, dy = x - cx, y - band_cy
            # The headband: an annulus, the top plus a little of each side.
            if dy <= band_drop and abs(math.hypot(dx, dy) - band_r) <= band_t / 2:
                hits[y][x] = 1
                continue
            # An earcup at each end of the band, as a rounded rectangle.
            for side in (-1, 1):
                ex, ey = cx + side * band_r, cup_cy
                ox, oy = abs(x - ex) - (cup_w / 2 - cup_r), abs(y - ey) - (cup_h / 2 - cup_r)
                dist = math.hypot(max(ox, 0), max(oy, 0)) + min(max(ox, oy), 0)
                if dist <= cup_r:
                    hits[y][x] = 1
                    break

    out = [[0.0] * size for _ in range(size)]
    area = SUPERSAMPLE * SUPERSAMPLE
    for y in range(size):
        for x in range(size):
            total = 0
            for sy in range(SUPERSAMPLE):
                row = hits[y * SUPERSAMPLE + sy]
                for sx in range(SUPERSAMPLE):
                    total += row[x * SUPERSAMPLE + sx]
            out[y][x] = total / area
    return out


def dib(size, cov):
    """A BITMAPINFOHEADER icon image: bottom-up BGRA, then the AND mask."""
    header = struct.pack("<IiiHHIIiiII", 40, size, size * 2, 1, 32, 0, 0, 0, 0, 0, 0)

    rows = []
    for y in reversed(range(size)):
        row = bytearray()
        for x in range(size):
            a = int(round(cov[y][x] * 255))
            r, g, b = FOREGROUND
            # Premultiplication is not used by ICO; store the colour straight with its alpha.
            row += bytes((b, g, r, a))
        rows.append(bytes(row))
    xor = b"".join(rows)

    # The mask is vestigial for 32-bit icons — the alpha channel is what gets used — but
    # Windows still expects it to be present and correctly sized.
    stride = ((size + 31) // 32) * 4
    and_mask = b"\x00" * (stride * size)

    return header + xor + and_mask


def png(size, cov):
    def chunk(tag, payload):
        body = tag + payload
        return struct.pack(">I", len(payload)) + body + struct.pack(">I", zlib.crc32(body))

    raw = bytearray()
    for y in range(size):
        raw.append(0)
        for x in range(size):
            r, g, b = FOREGROUND
            raw += bytes((r, g, b, int(round(cov[y][x] * 255))))
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(bytes(raw), 9))
            + chunk(b"IEND", b""))


def main():
    frames = []
    for size in SIZES:
        cov = coverage(size)
        # 256 goes in as PNG to keep the file small; everything else must be a DIB.
        frames.append((size, png(size, cov) if size == 256 else dib(size, cov)))

    offset = 6 + 16 * len(frames)
    header = struct.pack("<HHH", 0, 1, len(frames))
    entries, blobs = b"", b""
    for size, blob in frames:
        entries += struct.pack("<BBBBHHII",
                               0 if size == 256 else size,
                               0 if size == 256 else size,
                               0, 0, 1, 32, len(blob), offset)
        blobs += blob
        offset += len(blob)

    out = Path(__file__).with_name("openinzone.ico")
    out.write_bytes(header + entries + blobs)
    print(f"wrote {out} ({len(header + entries + blobs)} bytes, {len(frames)} sizes)")


if __name__ == "__main__":
    main()
