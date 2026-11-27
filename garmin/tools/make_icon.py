#!/usr/bin/env python3
"""Generate garmin/resources/drawables/launcher_icon.png (40x40) using only the stdlib.

Run once from anywhere:  python garmin/tools/make_icon.py
Replace the PNG with your own artwork whenever you like.
"""
import os
import struct
import zlib

SIZE = 40
BG = (31, 111, 235)
FG = (255, 255, 255)
RADIUS = 9.0
TRI = ((14.0, 10.0), (14.0, 30.0), (31.0, 20.0))


def in_round_rect(x, y):
    cx = min(max(x, RADIUS), SIZE - RADIUS)
    cy = min(max(y, RADIUS), SIZE - RADIUS)
    return (x - cx) ** 2 + (y - cy) ** 2 <= RADIUS ** 2


def in_triangle(x, y, a, b, c):
    def sign(p1, p2, p3):
        return (p1[0] - p3[0]) * (p2[1] - p3[1]) - (p2[0] - p3[0]) * (p1[1] - p3[1])

    p = (x, y)
    d1, d2, d3 = sign(p, a, b), sign(p, b, c), sign(p, c, a)
    return not ((d1 < 0 or d2 < 0 or d3 < 0) and (d1 > 0 or d2 > 0 or d3 > 0))


def chunk(tag, data):
    return (struct.pack(">I", len(data)) + tag + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))


def main():
    rows = bytearray()
    for y in range(SIZE):
        rows.append(0)  # filter type: none
        for x in range(SIZE):
            px, py = x + 0.5, y + 0.5
            if not in_round_rect(px, py):
                rows += bytes((0, 0, 0, 0))
            elif in_triangle(px, py, *TRI):
                rows += bytes(FG + (255,))
            else:
                rows += bytes(BG + (255,))

    png = (b"\x89PNG\r\n\x1a\n"
           + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 6, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(bytes(rows), 9))
           + chunk(b"IEND", b""))

    out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                           "..", "resources", "drawables")
    os.makedirs(out_dir, exist_ok=True)
    out = os.path.normpath(os.path.join(out_dir, "launcher_icon.png"))
    with open(out, "wb") as f:
        f.write(png)
    print("Wrote", out)


if __name__ == "__main__":
    main()
