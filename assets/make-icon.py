#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-only
# Copyright (C) 2026 penguinwokrs
"""Generates assets/openinzone.ico. Standard library only, so it runs anywhere.

The mark is a headband arc with an earcup on each side, drawn at several sizes and
packed into one ICO. Windows accepts PNG-compressed ICO entries from Vista onwards.
"""
import math
import struct
import zlib
from pathlib import Path

SIZES = (16, 24, 32, 48, 64, 128, 256)
FOREGROUND = (0xE8, 0xEA, 0xED, 0xFF)


def draw(size):
    """Returns RGBA bytes for one square frame."""
    px = [[(0, 0, 0, 0)] * size for _ in range(size)]
    cx = cy = (size - 1) / 2
    band = size * 0.34          # headband radius
    thickness = max(1.0, size * 0.11)
    cup_w, cup_h = size * 0.16, size * 0.26

    for y in range(size):
        for x in range(size):
            dx, dy = x - cx, y - cy - size * 0.06
            # The headband: an annulus, upper half only.
            if dy <= 0 and abs(math.hypot(dx, dy) - band) <= thickness / 2:
                px[y][x] = FOREGROUND
                continue
            # An earcup at each end of the band.
            for side in (-1, 1):
                ex, ey = cx + side * band, cy + size * 0.10
                if abs(x - ex) <= cup_w / 2 and abs(y - ey) <= cup_h / 2:
                    px[y][x] = FOREGROUND

    return b"".join(bytes(c for pixel in row for c in pixel) for row in px)


def png(size, rgba):
    def chunk(tag, payload):
        body = tag + payload
        return struct.pack(">I", len(payload)) + body + struct.pack(">I", zlib.crc32(body))

    raw = b"".join(b"\x00" + rgba[y * size * 4:(y + 1) * size * 4] for y in range(size))
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9))
            + chunk(b"IEND", b""))


def main():
    frames = [(s, png(s, draw(s))) for s in SIZES]
    offset = 6 + 16 * len(frames)
    header = struct.pack("<HHH", 0, 1, len(frames))
    entries, blobs = b"", b""
    for size, blob in frames:
        entries += struct.pack("<BBBBHHII",
                               size if size < 256 else 0, size if size < 256 else 0,
                               0, 0, 1, 32, len(blob), offset)
        blobs += blob
        offset += len(blob)

    out = Path(__file__).with_name("openinzone.ico")
    out.write_bytes(header + entries + blobs)
    print(f"wrote {out} ({len(header + entries + blobs)} bytes, {len(frames)} sizes)")


if __name__ == "__main__":
    main()
