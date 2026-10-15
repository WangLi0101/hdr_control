#!/usr/bin/env python3
"""Original monitor artwork, rasterized at each tray DPI. Standard library only."""
import struct
from pathlib import Path

def inside(x, y):
    # Monitor outline, central signal glow, stem and base, in a 32x32 coordinate grid.
    frame = 3 <= x < 29 and 5 <= y < 23 and not (5 <= x < 27 and 7 <= y < 21)
    stand = 14 <= x < 18 and 23 <= y < 27 or 10 <= x < 22 and 27 <= y < 29
    signal = 9 <= x < 23 and 11 <= y < 17
    return frame or stand or signal

def bitmap(size):
    pixels = bytearray()
    for y in reversed(range(size)):
        for x in range(size):
            coverage = sum(inside((x + (sx+.5)/4)*32/size, (y+(sy+.5)/4)*32/size)
                           for sx in range(4) for sy in range(4)) / 16
            pixels += bytes((255, 199, 125, round(coverage*255)))
    stride = ((size+31)//32)*4
    header = struct.pack('<IiiHHIIiiII', 40, size, size*2, 1, 32, 0, len(pixels), 0, 0, 0, 0)
    return header + pixels + bytes(stride*size)

sizes = [16, 20, 24, 28, 32, 40, 48, 64, 256]
images = [bitmap(s) for s in sizes]
offset = 6 + 16*len(sizes)
entries = []
for size, data in zip(sizes, images):
    entries.append(struct.pack('<BBBBHHII', size % 256, size % 256, 0, 0, 1, 32, len(data), offset))
    offset += len(data)
path = Path(__file__).resolve().parents[1] / 'DisplayQuickControl/Resources/Icons/app.ico'
path.write_bytes(struct.pack('<HHH', 0, 1, len(sizes)) + b''.join(entries) + b''.join(images))
print(path)
