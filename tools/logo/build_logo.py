#!/usr/bin/env python3
"""Builds the CamelGraph logo set: SVG masters, PNG sizes, icons, lock-ups and social images.

Everything is drawn from ONE geometry (two node ends, two ring sockets and an S wire, on a 100 x 100 grid), so the set
cannot drift. Outputs go to assets/logo/ (committed). Run it again after changing a colour or the geometry below.

    cd tools/logo && npm ci          # once: installs @resvg/resvg-js, which draws the SVGs
    python tools/logo/build_logo.py --fonts <folder with GoogleSansFlex-*.ttf and ShareTech-Regular.ttf>

The fonts are the ones bimcamel.com uses (src/bimcamel-web/public/fonts in the BIMCamel repository, SIL Open Font
Licence). They are not stored here. Without them the mark, icons and PNGs are still built; the lock-ups and the
social images need them.

Requires: Python 3.9+, Pillow, fontTools, Node 18+.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys

from PIL import Image

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "assets" / "logo"
HERE = pathlib.Path(__file__).resolve().parent

# ---- colours ---------------------------------------------------------------------------------------------------
PLATE = "#0D0E11"        # the dark plate (the editor's own background)
WHITE = "#FFFFFF"
BLUE = "#2E9BFF"         # wire on dark
BLUE_LIGHT = "#1A73C5"   # wire on light (the editor's primary blue)
EDGE = "#CFD4DB"         # hairline around a white plate
NAVY_A, NAVY_B = "#0A1020", "#15233F"   # the site's social-card background

# ---- the mark --------------------------------------------------------------------------------------------------
# Standard drawing, for 48 px and up. `small` is the same idea with a thicker wire, bigger sockets and taller node
# ends, so it still reads at 16 to 32 px.
STANDARD = dict(ny=(22, 50, 50, 78), wire_w=6, ring_r=7.0, ring_w=5.0)
SMALL = dict(ny=(16, 56, 44, 84), wire_w=9, ring_r=9.5, ring_w=7.0)


def _geometry(small: bool):
    g = SMALL if small else STANDARD
    ly0, ly1, ry0, ry1 = g["ny"]
    r, w = g["ring_r"], g["ring_w"]
    hole = r - w / 2                     # radius of the hole in the ring, cut through the node end
    lcy, rcy = (ly0 + ly1) / 2, (ry0 + ry1) / 2
    cy_l, cy_r = lcy, rcy
    lcx, rcx = 16.0, 84.0

    def node_left():
        # the right edge has a half-circle notch where the socket sits, so the hole is clear on any background
        top = cy_l - hole - ly0 - 6
        bottom = ly1 - 6 - (cy_l + hole)
        return (f"M-6 {ly0} h16 a6 6 0 0 1 6 6 v{top} a{hole} {hole} 0 0 0 0 {2 * hole} v{bottom} a6 6 0 0 1 -6 6 h-16 z")

    def node_right():
        top = cy_r - hole - ry0 - 6
        bottom = ry1 - 6 - (cy_r + hole)
        return (f"M106 {ry0} h-16 a6 6 0 0 0 -6 6 v{top} a{hole} {hole} 0 0 1 0 {2 * hole} v{bottom} a6 6 0 0 0 6 6 h16 z")

    # The wire starts and ends inside the ring's band (same colour), so no seam shows and the hole stays clear.
    x0, x1 = lcx + r, rcx - r
    wire = f"M{x0} {lcy} C56 {lcy} 44 {rcy} {x1} {rcy}"
    return dict(node_left=node_left(), node_right=node_right(), wire=wire, wire_w=g["wire_w"],
                rings=[(lcx, lcy), (rcx, rcy)], ring_r=r, ring_w=w)


def mark_group(node: str, wire: str, small: bool = False, transform: str = "") -> str:
    g = _geometry(small)
    t = f' transform="{transform}"' if transform else ""
    rings = "".join(
        f'<circle cx="{cx}" cy="{cy}" r="{g["ring_r"]}" fill="none" stroke="{wire}" stroke-width="{g["ring_w"]}"/>'
        for cx, cy in g["rings"]
    )
    return (f'<g{t}><path d="{g["node_left"]}" fill="{node}"/>'
            f'<path d="{g["node_right"]}" fill="{node}"/>'
            f'<path d="{g["wire"]}" fill="none" stroke="{wire}" stroke-width="{g["wire_w"]}" stroke-linecap="round"/>'
            f"{rings}</g>")


def mark_svg(*, plate: str | None = PLATE, node: str = WHITE, wire: str = BLUE, shape: str = "squircle",
             small: bool = False, edge: str | None = None, scale: float = 1.0, size: int = 100) -> str:
    """One mark. shape: squircle (rounded square), round, square (full-bleed, for icons the OS masks), none."""
    s = ""
    clip = ""
    if shape == "round":
        clip = '<clipPath id="c"><circle cx="50" cy="50" r="50"/></clipPath>'
        if plate:
            stroke = f' stroke="{edge}" stroke-width="1"' if edge else ""
            s = f'<circle cx="50" cy="50" r="{49.5 if edge else 50}" fill="{plate}"{stroke}/>'
    elif shape == "square":
        clip = '<clipPath id="c"><rect width="100" height="100"/></clipPath>'
        if plate:
            s = f'<rect width="100" height="100" fill="{plate}"/>'
    elif shape == "squircle":
        clip = '<clipPath id="c"><rect width="100" height="100" rx="20"/></clipPath>'
        if plate:
            stroke = f' stroke="{edge}" stroke-width="1"' if edge else ""
            off = 0.5 if edge else 0
            s = (f'<rect x="{off}" y="{off}" width="{100 - 2 * off}" height="{100 - 2 * off}" '
                 f'rx="{20 - off}" fill="{plate}"{stroke}/>')
    else:  # none: the node ends run off the edge of the square canvas
        clip = '<clipPath id="c"><rect width="100" height="100"/></clipPath>'
    tr = f"translate({50 - 50 * scale} {50 - 50 * scale}) scale({scale})" if scale != 1.0 else ""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="{size}" height="{size}">'
            f'<defs>{clip}</defs>{s}<g clip-path="url(#c)">{mark_group(node, wire, small, tr)}</g></svg>')


# variants: name -> (svg kwargs). `small` twins are made for the plate variants used at 16 to 32 px.
VARIANTS = {
    "camelgraph-mark": dict(),                                                     # the logo: 5a
    "camelgraph-mark-on-dark": dict(plate=None, shape="none"),                      # no plate, for dark backgrounds
    "camelgraph-mark-on-light": dict(plate=None, shape="none", node=PLATE, wire=BLUE_LIGHT),
    "camelgraph-mark-light": dict(plate=WHITE, node=PLATE, wire=BLUE_LIGHT, edge=EDGE),
    "camelgraph-mark-round": dict(shape="round"),
    "camelgraph-mark-mono-white": dict(plate=None, shape="none", node=WHITE, wire=WHITE),
    "camelgraph-mark-mono-black": dict(plate=None, shape="none", node=PLATE, wire=PLATE),
    "camelgraph-mark-mono-plate": dict(plate=PLATE, node=WHITE, wire=WHITE),       # ribbon button, white on black
    "camelgraph-mark-mono-plate-inverse": dict(plate=WHITE, node=PLATE, wire=PLATE, edge=EDGE),
    "camelgraph-mark-square": dict(shape="square"),                                 # full-bleed, the OS rounds it
    "camelgraph-mark-maskable": dict(shape="square", scale=0.78),                   # Android / PWA safe zone
}
SMALL_TWINS = ["camelgraph-mark", "camelgraph-mark-light", "camelgraph-mark-mono-plate", "camelgraph-mark-mono-plate-inverse",
               "camelgraph-mark-round", "camelgraph-mark-square"]


# ---- text as outlines (for the lock-ups) ---------------------------------------------------------------------
def text_path(font_path: pathlib.Path, text: str, size: float, x: float, y: float, tracking: float = 0.0):
    from fontTools.pens.svgPathPen import SVGPathPen
    from fontTools.pens.transformPen import TransformPen
    from fontTools.ttLib import TTFont

    font = TTFont(str(font_path))
    gs, cmap = font.getGlyphSet(), font.getBestCmap()
    scale = size / font["head"].unitsPerEm
    pen = SVGPathPen(gs)
    cursor = x
    for ch in text:
        glyph = gs[cmap[ord(ch)]]
        glyph.draw(TransformPen(pen, (scale, 0, 0, -scale, cursor, y)))
        cursor += glyph.width * scale + tracking
    return pen.getCommands(), cursor


def lockup_svg(share_tech: pathlib.Path, *, dark_background: bool, stacked: bool = False) -> str:
    """The mark with the name set in outlines: 'CamelGraph' and a small 'by BIMCamel'."""
    text = WHITE if dark_background else PLATE
    muted = "#9AA3AF" if dark_background else "#4B525C"
    mark = mark_svg(size=120, edge="#2A2F38" if dark_background else None)
    if stacked:
        name, w = text_path(share_tech, "CamelGraph", 64, 0, 0, 1.5)
        by, w2 = text_path(share_tech, "by BIMCamel", 22, 0, 0, 1.0)
        width = max(w, w2, 120)
        inner = (f'<g transform="translate({(width - 120) / 2} 0)">{mark}</g>'
                 f'<path transform="translate({(width - w) / 2} 190)" d="{name}" fill="{text}"/>'
                 f'<path transform="translate({(width - w2) / 2} 226)" d="{by}" fill="{muted}"/>')
        return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width:.0f} 240" width="{width:.0f}" height="240">{inner}</svg>')
    name, w = text_path(share_tech, "CamelGraph", 64, 0, 0, 1.5)
    by, _ = text_path(share_tech, "by BIMCamel", 22, 0, 0, 1.0)
    width = 120 + 28 + w
    inner = (f"{mark}"
             f'<path transform="translate(148 70)" d="{name}" fill="{text}"/>'
             f'<path transform="translate(150 100)" d="{by}" fill="{muted}"/>')
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width:.0f} 120" width="{width:.0f}" height="120">{inner}</svg>'


# ---- social images -------------------------------------------------------------------------------------------
def _bg(w: int, h: int) -> str:
    return (f'<defs><linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="{NAVY_A}"/>'
            f'<stop offset="1" stop-color="{NAVY_B}"/></linearGradient>'
            f'<radialGradient id="wash" cx="0.85" cy="0.15" r="0.75"><stop offset="0" stop-color="{BLUE}" stop-opacity="0.28"/>'
            f'<stop offset="1" stop-color="{BLUE}" stop-opacity="0"/></radialGradient>'
            f'<pattern id="grid" width="48" height="48" patternUnits="userSpaceOnUse"><path d="M48 0H0V48" fill="none" '
            f'stroke="#94a3b8" stroke-opacity="0.06"/></pattern></defs>'
            f'<rect width="{w}" height="{h}" fill="url(#bg)"/><rect width="{w}" height="{h}" fill="url(#grid)"/>'
            f'<rect width="{w}" height="{h}" fill="url(#wash)"/>')


def _mark_nested(x: float, y: float, size: float) -> str:
    return mark_svg(size=int(size)).replace("<svg ", f'<svg x="{x}" y="{y}" ', 1)


def _text(x, y, s, size, fill, weight=400, anchor="start", spacing=0, family="Google Sans Flex"):
    from xml.sax.saxutils import escape
    return (f'<text x="{x}" y="{y}" fill="{fill}" font-family="{family}" font-size="{size}" font-weight="{weight}" '
            f'letter-spacing="{spacing}" text-anchor="{anchor}">{escape(s)}</text>')


TAGLINE = "Visual programming for Navisworks"
SUBLINE = "by BIMCamel · previously Dyncamelo"
URL = "bimcamel.com/plugins/dyncamelo"


def social_wide(w: int, h: int, *, footer: bool = True) -> str:
    """Mark on the left, text on the right; for 1200x630, 1280x640 and the banners."""
    pad = round(h * 0.13)
    mark = round(h * 0.46) if h > 400 else round(h * 0.52)
    mx, my = pad + round(h * 0.02), (h - mark) // 2 - (round(h * 0.02) if footer and h > 400 else 0)
    tx = mx + mark + round(h * 0.09)
    title = round(h * (0.145 if h > 400 else 0.2))
    tag = round(title * 0.40)
    sub = round(title * 0.30)
    ty = my + round(mark * 0.50)
    s = _bg(w, h) + _mark_nested(mx, my, mark)
    if h > 400:
        s += _text(tx, ty - round(title * 0.95), "NAVISWORKS ADD-IN", round(title * 0.27), BLUE, 600, spacing=round(title * 0.05))
    s += _text(tx, ty + round(title * 0.28), "CamelGraph", title, WHITE, 700, spacing=-1)
    s += _text(tx, ty + round(title * 0.28) + round(tag * 1.55), TAGLINE, tag, "#CBD5E1")
    s += _text(tx, ty + round(title * 0.28) + round(tag * 1.55) + round(sub * 1.8), SUBLINE, sub, "#94A3B8", 500)
    if footer:
        s += _text(pad, h - round(h * 0.08), URL, round(h * 0.04), "#94A3B8", 600)
    s += f'<rect x="0" y="{h - 8}" width="{w}" height="8" fill="{BLUE}"/>'
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}">{s}</svg>'


def social_square(n: int) -> str:
    mark = round(n * 0.34)
    s = _bg(n, n) + _mark_nested((n - mark) // 2, round(n * 0.17), mark)
    s += _text(n // 2, round(n * 0.64), "CamelGraph", round(n * 0.105), WHITE, 700, "middle", -1)
    s += _text(n // 2, round(n * 0.64) + round(n * 0.07), TAGLINE, round(n * 0.037), "#CBD5E1", 400, "middle")
    s += _text(n // 2, round(n * 0.64) + round(n * 0.125), SUBLINE, round(n * 0.028), "#94A3B8", 500, "middle")
    s += _text(n // 2, n - round(n * 0.07), URL, round(n * 0.03), "#94A3B8", 600, "middle")
    s += f'<rect x="0" y="{n - 10}" width="{n}" height="10" fill="{BLUE}"/>'
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {n} {n}" width="{n}" height="{n}">{s}</svg>'


# ---- rendering ---------------------------------------------------------------------------------------------------
def render(jobs: list[dict], font_files: list[str]) -> None:
    """jobs: [{"svg": path, "out": path, "width": px}] -> PNG files, drawn by resvg through Node."""
    if not jobs:
        return
    payload = json.dumps({"jobs": jobs, "fonts": font_files, "default": "Google Sans Flex"})
    subprocess.run(["node", str(HERE / "render.mjs")], input=payload.encode(), check=True, cwd=str(HERE))


def write(path: pathlib.Path, text: str) -> pathlib.Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    return path


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--fonts", type=pathlib.Path, help="folder with GoogleSansFlex-*.ttf and ShareTech-Regular.ttf")
    args = ap.parse_args()

    svg_dir, png_dir, ico_dir, social_dir = OUT / "svg", OUT / "png", OUT / "ico", OUT / "social"
    jobs: list[dict] = []

    # 1. the mark variants: SVG, and PNG at the sizes each is used at
    sizes_all = [16, 24, 32, 48, 64, 80, 96, 120, 128, 180, 192, 256, 384, 512, 1024]
    for name, kw in VARIANTS.items():
        write(svg_dir / f"{name}.svg", mark_svg(**kw))
        if name in SMALL_TWINS:
            write(svg_dir / f"{name}-small.svg", mark_svg(**{**kw, "small": True}))
    for name in ["camelgraph-mark", "camelgraph-mark-light", "camelgraph-mark-round", "camelgraph-mark-mono-plate",
                 "camelgraph-mark-mono-plate-inverse", "camelgraph-mark-square"]:
        for px in sizes_all:
            src = f"{name}-small" if (px <= 32 and name in SMALL_TWINS) else name
            jobs.append(dict(svg=str(svg_dir / f"{src}.svg"), out=str(png_dir / f"{name}-{px}.png"), width=px))
    for name in ["camelgraph-mark-on-dark", "camelgraph-mark-on-light", "camelgraph-mark-mono-white", "camelgraph-mark-mono-black"]:
        for px in (64, 128, 256, 512, 1024):
            jobs.append(dict(svg=str(svg_dir / f"{name}.svg"), out=str(png_dir / f"{name}-{px}.png"), width=px))
    for px in (48, 64):     # the small drawing, a little larger: for chips of 24 to 32 px at up to 200% scaling
        jobs.append(dict(svg=str(svg_dir / "camelgraph-mark-small.svg"), out=str(png_dir / f"camelgraph-mark-small-{px}.png"), width=px))
    jobs.append(dict(svg=str(svg_dir / "camelgraph-mark-maskable.svg"), out=str(png_dir / "camelgraph-mark-maskable-512.png"), width=512))
    jobs.append(dict(svg=str(svg_dir / "camelgraph-mark-maskable.svg"), out=str(png_dir / "camelgraph-mark-maskable-192.png"), width=192))

    # 2. the fonts, for the lock-ups and the social images
    font_files: list[str] = []
    share_tech = None
    if args.fonts and args.fonts.is_dir():
        font_files = [str(p) for p in sorted(args.fonts.glob("*.ttf"))]
        share_tech = args.fonts / "ShareTech-Regular.ttf"
    if share_tech and share_tech.exists():
        for dark in (True, False):
            tag = "on-dark" if dark else "on-light"
            write(svg_dir / f"camelgraph-lockup-{tag}.svg", lockup_svg(share_tech, dark_background=dark))
            write(svg_dir / f"camelgraph-lockup-stacked-{tag}.svg", lockup_svg(share_tech, dark_background=dark, stacked=True))
            for px in (600, 1200):
                jobs.append(dict(svg=str(svg_dir / f"camelgraph-lockup-{tag}.svg"), out=str(png_dir / f"camelgraph-lockup-{tag}-{px}.png"), width=px))
            for px in (400, 800):
                jobs.append(dict(svg=str(svg_dir / f"camelgraph-lockup-stacked-{tag}.svg"), out=str(png_dir / f"camelgraph-lockup-stacked-{tag}-{px}.png"), width=px))
    else:
        print("note: no fonts given, so the lock-ups are skipped", file=sys.stderr)
    if font_files:
        for name, (w, h), svg in [
            ("og-1200x630", (1200, 630), social_wide(1200, 630)),
            ("github-social-1280x640", (1280, 640), social_wide(1280, 640)),
            ("linkedin-banner-1584x396", (1584, 396), social_wide(1584, 396, footer=False)),
            ("x-header-1500x500", (1500, 500), social_wide(1500, 500, footer=False)),
            ("post-square-1080", (1080, 1080), social_square(1080)),
        ]:
            write(social_dir / f"{name}.svg", svg)
            jobs.append(dict(svg=str(social_dir / f"{name}.svg"), out=str(social_dir / f"{name}.png"), width=w))
        jobs.append(dict(svg=str(svg_dir / "camelgraph-mark-round.svg"), out=str(social_dir / "avatar-round-800.png"), width=800))
        jobs.append(dict(svg=str(svg_dir / "camelgraph-mark.svg"), out=str(social_dir / "avatar-square-800.png"), width=800))

    render(jobs, font_files)

    # 3. icons: one .ico per use, each size drawn at that size (the small drawing up to 32 px)
    def ico(name: str, variant: str, sizes: list[int]) -> None:
        ico_dir.mkdir(parents=True, exist_ok=True)
        frames = [Image.open(png_dir / f"{variant}-{px}.png").convert("RGBA") for px in sizes]
        frames[-1].save(ico_dir / name, format="ICO", sizes=[f.size for f in frames], append_images=frames[:-1])

    ico("camelgraph.ico", "camelgraph-mark", [16, 24, 32, 48, 64, 128, 256])
    ico("camelgraph-mono.ico", "camelgraph-mark-mono-plate", [16, 24, 32, 48, 64, 128, 256])
    ico("favicon.ico", "camelgraph-mark", [16, 32, 48])

    # sanity: what was written
    for p in sorted(ico_dir.glob("*.ico")):
        print(p.relative_to(ROOT), sorted(Image.open(p).info.get("sizes", [])))
    print("files:", sum(1 for _ in OUT.rglob("*") if _.is_file()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
