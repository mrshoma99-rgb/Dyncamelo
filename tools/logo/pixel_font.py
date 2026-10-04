"""The pixel letters of the CamelGraph name, drawn on the same grid as the BIMCamel wordmark on bimcamel.com.

The wordmark there (src/bimcamel-web/src/assets/logo-text.svg) is pixel art on a 4-unit cell: strokes three cells thick, a
cap height of 15 cells, corners cut by a diagonal touch instead of being rounded. C, a, m, e and l below are read from it,
cell by cell; G, r, p and h are drawn in the same way so "CamelGraph" sits next to "BIMCamel" without a visible seam.

Every glyph is a list of rows, '#' for a filled cell. `top` is the row the glyph starts at (0 = cap line, 4 = x-height line,
15 = baseline); a row beyond 14 is a descender.
"""
from __future__ import annotations

CAP = 15          # cells from the cap line to the baseline
DESCENDER = 4     # cells below the baseline (the tail of the p)
GAP = 1           # empty cells between two letters (as in the BIMCamel wordmark)

# glyph: (first row, rows)
GLYPHS: dict[str, tuple[int, list[str]]] = {
    "C": (0, [
        "...##########.",
        "...##########.",
        "...##########.",
        "###........###",
        "###........###",
        "###...........",
        "###...........",
        "###...........",
        "###...........",
        "###........###",
        "###........###",
        "###........###",
        "...##########.",
        "...##########.",
        "...##########.",
    ]),
    "G": (0, [
        "...##########.",
        "...##########.",
        "...##########.",
        "###........###",
        "###........###",
        "###...........",
        "###...........",
        "###....#######",
        "###....#######",
        "###....#######",
        "###........###",
        "###........###",
        "...##########.",
        "...##########.",
        "...##########.",
    ]),
    "a": (3, [
        "########...",
        "########...",
        "........###",
        "........###",
        "........###",
        "...########",
        "###.....###",
        "###.....###",
        "###.....###",
        "...########",
        "...########",
        "...########",
    ]),
    "m": (4, [
        "########...####...",
        "########...####...",
        "########...####...",
        "###.....###....###",
        "###.....###....###",
        "###.....###....###",
        "###.....###....###",
        "###.....###....###",
        "###.....###....###",
        "###.....###....###",
        "###.....###....###",
    ]),
    "e": (4, [
        "...#######",
        "...#######",
        "...#######",
        "###....###",
        "#######...",
        "###.......",
        "###.......",
        "###....###",
        "...#######",
        "...#######",
        "...#######",
    ]),
    "l": (0, ["###"] * 15),
    "r": (4, [
        "########",
        "########",
        "########",
        "###.....",
        "###.....",
        "###.....",
        "###.....",
        "###.....",
        "###.....",
        "###.....",
        "###.....",
    ]),
    "p": (4, [
        "########...",
        "########...",
        "########...",
        "###.....###",
        "###.....###",
        "###.....###",
        "###.....###",
        "###.....###",
        "########...",
        "########...",
        "########...",
        "###........",
        "###........",
        "###........",
        "###........",
    ]),
    "h": (0, [
        "###........",
        "###........",
        "###........",
        "###........",
        "########...",
        "########...",
        "########...",
        "###.....###",
        "###.....###",
        "###.....###",
        "###.....###",
        "###.....###",
        "###.....###",
        "###.....###",
        "###.....###",
    ]),
    # the letters of "by BIMCamel" are not needed: the small line is set in Share Tech like the body text of the site
}


def _cells(text: str):
    """Yield (column, row) of every filled cell of `text`, with the letters laid out left to right."""
    x = 0
    for ch in text:
        top, rows = GLYPHS[ch]
        width = max(len(r) for r in rows)
        for j, row in enumerate(rows):
            for i, c in enumerate(row):
                if c == "#":
                    yield x + i, top + j
        x += width + GAP


def size_cells(text: str) -> tuple[int, int]:
    """(width, height) of `text` in cells, the descender included."""
    width = sum(max(len(r) for r in GLYPHS[ch][1]) for ch in text) + GAP * (len(text) - 1)
    return width, CAP + DESCENDER


def _rectangles(cells: set[tuple[int, int]]):
    """Cover the cells with as few rectangles as a greedy sweep finds: (column, row, width, height)."""
    left = set(cells)
    out = []
    for (x, y) in sorted(cells, key=lambda c: (c[1], c[0])):
        if (x, y) not in left:
            continue
        w = 1
        while (x + w, y) in left:
            w += 1
        h = 1
        while all((x + i, y + h) in left for i in range(w)):
            h += 1
        for j in range(h):
            for i in range(w):
                left.discard((x + i, y + j))
        out.append((x, y, w, h))
    return out


def path_data(text: str, cell: float, x: float = 0.0, y: float = 0.0) -> str:
    """The text as one SVG/XAML path (nonzero fill). (x, y) is the top-left of the cap line; `cell` is the cell size."""
    parts = []
    for (cx, cy, w, h) in _rectangles(set(_cells(text))):
        parts.append(f"M{x + cx * cell:g} {y + cy * cell:g}h{w * cell:g}v{h * cell:g}h{-w * cell:g}z")
    return "".join(parts)
