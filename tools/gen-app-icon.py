#!/usr/bin/env python3
"""Gera src/MonitorVirtual.App/app.ico (dois projetores + junta azul)."""
from __future__ import annotations

import struct
import zlib
from pathlib import Path

# moldura bege, telas cinza, junta azul — visual do atalho / taskbar da igreja
FRAME = (228, 220, 204, 255)
INNER = (40, 44, 50, 255)
SCREEN = (188, 190, 196, 255)
JUNTA = (40, 118, 214, 255)
STAND = (120, 118, 112, 255)


def lerp(a: float, b: float, t: float) -> float:
    return a + (b - a) * t


def blend(dst: tuple[int, int, int, int], src: tuple[int, int, int, int]) -> tuple[int, int, int, int]:
    if src[3] == 0:
        return dst
    if src[3] == 255:
        return src
    t = src[3] / 255.0
    return (
        int(lerp(dst[0], src[0], t)),
        int(lerp(dst[1], src[1], t)),
        int(lerp(dst[2], src[2], t)),
        min(255, dst[3] + src[3]),
    )


def fill_rect(px: list[list[tuple[int, int, int, int]]], x: float, y: float, w: float, h: float, color):
    size = len(px)
    x0, y0 = int(round(x)), int(round(y))
    x1, y1 = int(round(x + w)), int(round(y + h))
    for yy in range(max(0, y0), min(size, y1)):
        row = px[yy]
        for xx in range(max(0, x0), min(size, x1)):
            row[xx] = blend(row[xx], color)


def fill_round(px: list[list[tuple[int, int, int, int]]], x: float, y: float, w: float, h: float, radius: float, color):
    size = len(px)
    r = max(1.0, min(radius, w / 2, h / 2))
    x0, y0, x1, y1 = x, y, x + w, y + h
    for yy in range(max(0, int(y0)), min(size, int(y1) + 1)):
        cy = yy + 0.5
        row = px[yy]
        for xx in range(max(0, int(x0)), min(size, int(x1) + 1)):
            cx = xx + 0.5
            dx = 0.0
            dy = 0.0
            if cx < x0 + r:
                dx = (x0 + r) - cx
            elif cx > x1 - r:
                dx = cx - (x1 - r)
            if cy < y0 + r:
                dy = (y0 + r) - cy
            elif cy > y1 - r:
                dy = cy - (y1 - r)
            if dx * dx + dy * dy <= r * r and x0 <= cx <= x1 and y0 <= cy <= y1:
                row[xx] = blend(row[xx], color)


def paint(size: int) -> list[list[tuple[int, int, int, int]]]:
    px = [[(0, 0, 0, 0) for _ in range(size)] for _ in range(size)]
    s = size / 32.0
    fill_round(px, 1.5 * s, 1.5 * s, 29 * s, 29 * s, 3.2 * s, FRAME)
    fill_round(px, 4 * s, 4.5 * s, 24 * s, 17.5 * s, 1.6 * s, INNER)
    fill_rect(px, 5.2 * s, 5.8 * s, 8.6 * s, 14.2 * s, SCREEN)
    fill_rect(px, 18.2 * s, 5.8 * s, 8.6 * s, 14.2 * s, SCREEN)
    fill_rect(px, 14.2 * s, 5.4 * s, 3.6 * s, 15 * s, JUNTA)
    fill_rect(px, 8.4 * s, 22.6 * s, 2.4 * s, 2.2 * s, STAND)
    fill_rect(px, 6.6 * s, 24.8 * s, 6 * s, 1.8 * s, STAND)
    fill_rect(px, 21.2 * s, 22.6 * s, 2.4 * s, 2.2 * s, STAND)
    fill_rect(px, 19.4 * s, 24.8 * s, 6 * s, 1.8 * s, STAND)
    return px


def png_chunk(tag: bytes, data: bytes) -> bytes:
    crc = zlib.crc32(tag + data) & 0xFFFFFFFF
    return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", crc)


def write_png(px: list[list[tuple[int, int, int, int]]]) -> bytes:
    size = len(px)
    raw = bytearray()
    for row in px:
        raw.append(0)
        for r, g, b, a in row:
            raw.extend((r, g, b, a))
    compressed = zlib.compress(bytes(raw), 9)
    return b"".join(
        [
            b"\x89PNG\r\n\x1a\n",
            png_chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0)),
            png_chunk(b"IDAT", compressed),
            png_chunk(b"IEND", b""),
        ]
    )


def write_ico(path: Path, sizes: list[int]) -> None:
    images = [write_png(paint(s)) for s in sizes]
    count = len(sizes)
    offset = 6 + 16 * count
    entries = bytearray()
    for size, data in zip(sizes, images):
        w = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    path.write_bytes(struct.pack("<HHH", 0, 1, count) + entries + b"".join(images))


def main() -> None:
    root = Path(__file__).resolve().parents[1]
    out = root / "src" / "MonitorVirtual.App" / "app.ico"
    write_ico(out, [16, 24, 32, 48, 256])
    print(f"escrito {out} ({out.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
