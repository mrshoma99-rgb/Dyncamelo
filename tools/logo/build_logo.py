#!/usr/bin/env python3
"""Builds the CamelGraph logo set: SVG masters, PNG sizes, icons, lock-ups and social images.

Everything is drawn from ONE geometry (two node ends, two ring sockets and a wire that climbs into a hump before it settles
into the right socket, on a 100 x 100 grid), so the set cannot drift. Outputs go to assets/logo/ (committed). Run it again after changing a colour or the geometry below.

    cd tools/logo && npm ci          # once: installs @resvg/resvg-js, which draws the SVGs
    python tools/logo/build_logo.py --fonts <folder with GoogleSansFlex-*.ttf and ShareTech-Regular.ttf>

The fonts are the ones bimcamel.com uses (src/bimcamel-web/public/fonts in the BIMCamel repository, SIL Open Font
Licence). They are not stored here. Without them the mark, icons and PNGs are still built; the lock-ups and the
social images need them.

The name "CamelGraph" is set in the pixel letters of pixel_font.py (the same grid as the BIMCamel wordmark); the small line
"by BIMCamel" is Share Tech, like the body text of the site.

Requires: Python 3.9+, Pillow, fontTools, Node 18+.
"""
from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys

from PIL import Image

import pixel_font

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "assets" / "logo"
HERE = pathlib.Path(__file__).resolve().parent

# ---- colours ---------------------------------------------------------------------------------------------------
PLATE = "#0D0E11"        # the dark of the node ends on a light background, and the flat dark plate of the mono icons
PLATE_TOP, PLATE_BOTTOM = "#22356B", "#0A0C12"   # the dark plate is not flat black: a diagonal wash from deep blue to near-black
GRAD = "grad"            # `plate=GRAD` paints the gradient above
WHITE = "#FFFFFF"
BLUE = "#2E9BFF"         # wire on dark
BLUE_LIGHT = "#1A73C5"   # wire on light (the editor's primary blue)
EDGE = "#CFD4DB"         # hairline around a white plate
NAVY_A, NAVY_B = "#0A1020", "#15233F"   # the site's social-card background

# ---- the mark --------------------------------------------------------------------------------------------------
# Standard drawing, for 48 px and up. `small` is the same idea with a thicker wire, bigger sockets and taller node
# ends, so it still reads at 16 to 32 px. The left socket is low, the right one high; the wire climbs out of the left
# one, humps over and drops into the right one with a small dip.
STANDARD = dict(ny=(46, 74, 26, 54), wire_w=6, ring_r=7.0, ring_w=5.0)
SMALL = dict(ny=(40, 80, 20, 60), wire_w=9, ring_r=9.5, ring_w=7.0)


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

    # The wire starts and ends inside the ring's band (same colour), so no seam shows and the hole stays clear: it leaves
    # the left ring at its right-hand edge, going straight up (a tangent), and enters the right ring from the left.
    sx = lcx + r
    x1 = rcx - r
    wire = f"M{sx:g} {lcy:g} C{sx:g} 14 56 12 58 {rcy:g} C60 {rcy + 14:g} 70 {rcy:g} {x1:g} {rcy:g}"
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
    fill = "url(#cgp)" if plate == GRAD else plate
    if plate == GRAD:
        clip += (f'<linearGradient id="cgp" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="100" y2="100"><stop offset="0" stop-color="{PLATE_TOP}"/>'
                 f'<stop offset="1" stop-color="{PLATE_BOTTOM}"/></linearGradient>')
    if shape == "round":
        clip += '<clipPath id="c"><circle cx="50" cy="50" r="50"/></clipPath>'
        if plate:
            stroke = f' stroke="{edge}" stroke-width="1"' if edge else ""
            s = f'<circle cx="50" cy="50" r="{49.5 if edge else 50}" fill="{fill}"{stroke}/>'
    elif shape == "square":
        clip += '<clipPath id="c"><rect width="100" height="100"/></clipPath>'
        if plate:
            s = f'<rect width="100" height="100" fill="{fill}"/>'
    elif shape == "squircle":
        clip += '<clipPath id="c"><rect width="100" height="100" rx="20"/></clipPath>'
        if plate:
            stroke = f' stroke="{edge}" stroke-width="1"' if edge else ""
            off = 0.5 if edge else 0
            s = (f'<rect x="{off}" y="{off}" width="{100 - 2 * off}" height="{100 - 2 * off}" '
                 f'rx="{20 - off}" fill="{fill}"{stroke}/>')
    else:  # none: the node ends run off the edge of the square canvas
        clip += '<clipPath id="c"><rect width="100" height="100"/></clipPath>'
    tr = f"translate({50 - 50 * scale} {50 - 50 * scale}) scale({scale})" if scale != 1.0 else ""
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" width="{size}" height="{size}">'
            f'<defs>{clip}</defs>{s}<g clip-path="url(#c)">{mark_group(node, wire, small, tr)}</g></svg>')


# ---- the Player mark: a play triangle made of overlapped wires ----------------------------------------------------
# A blue wire runs up the left edge from one ring socket to another; a second wire (the node-end colour) makes the two
# sloping sides of the triangle. It passes over the blue wire at the top and under it at the bottom, so the two read as
# woven. `small` is the thicker drawing for 16 to 32 px.
PLAYER = dict(x=28.0, top=30.0, bottom=70.0, apex=76.0, mid=50.0, wire_w=6, ring_r=7.0, ring_w=5.0, rings=(14.0, 86.0), gap=4.0)
PLAYER_SMALL = dict(x=30.0, top=32.0, bottom=68.0, apex=75.0, mid=50.0, wire_w=9, ring_r=9.5, ring_w=7.0, rings=(13.0, 87.0), gap=3.0)


def _player_geometry(small: bool):
    g = PLAYER_SMALL if small else PLAYER
    x, t, b, a, m = g["x"], g["top"], g["bottom"], g["apex"], g["mid"]
    r, rw = g["ring_r"], g["ring_w"]
    r0, r1 = g["rings"]
    # the vertical wire joins the two rings at the points of their bands that face each other
    vert = f"M{x:g} {r0 + r:g} V{r1 - r:g}"
    tri = f"M{x:g} {t:g} L{a:g} {m:g} L{x:g} {b:g}"
    top_cut = f"M{x:g} {t:g} L{(x + a) / 2:g} {(t + m) / 2:g}"        # where the triangle passes over the vertical wire
    bottom_cut = f"M{x:g} {b - 10:g} V{b + 10:g}"                      # where the vertical wire passes over the triangle
    return dict(vert=vert, tri=tri, top_cut=top_cut, bottom_cut=bottom_cut, wire_w=g["wire_w"], gap=g["gap"], ring_r=r, ring_w=rw,
                rings=[(x, r0), (x, r1)])


def player_group(tri_col: str, wire_col: str, small: bool = False, transform: str = "", uid: str = "p") -> str:
    """The Player mark's drawing: masks cut a gap where one wire passes over the other, so it works on any background."""
    g = _player_geometry(small)
    w = g["wire_w"]
    gap = w + 2 * g["gap"]
    t = f' transform="{transform}"' if transform else ""
    defs = (f'<mask id="{uid}a" maskUnits="userSpaceOnUse" x="-10" y="-10" width="120" height="120"><rect x="-10" y="-10" width="120" height="120" fill="#fff"/>'
            f'<path d="{g["top_cut"]}" fill="none" stroke="#000" stroke-width="{gap}" stroke-linecap="round"/></mask>'
            f'<mask id="{uid}b" maskUnits="userSpaceOnUse" x="-10" y="-10" width="120" height="120"><rect x="-10" y="-10" width="120" height="120" fill="#fff"/>'
            f'<path d="{g["bottom_cut"]}" fill="none" stroke="#000" stroke-width="{gap}" stroke-linecap="round"/></mask>')
    rings = "".join(f'<circle cx="{cx}" cy="{cy}" r="{g["ring_r"]}" fill="none" stroke="{wire_col}" stroke-width="{g["ring_w"]}"/>'
                    for cx, cy in g["rings"])
    return (f'<defs>{defs}</defs><g{t}>'
            f'<path d="{g["vert"]}" fill="none" stroke="{wire_col}" stroke-width="{w}" stroke-linecap="round" mask="url(#{uid}a)"/>'
            f'<path d="{g["tri"]}" fill="none" stroke="{tri_col}" stroke-width="{w}" stroke-linecap="round" stroke-linejoin="round" mask="url(#{uid}b)"/>'
            f'{rings}</g>')


def player_svg(*, plate: str | None = GRAD, tri: str = WHITE, wire: str = BLUE, shape: str = "squircle", small: bool = False,
               edge: str | None = None, size: int = 100) -> str:
    """The Player mark on the same plates as the main mark (the plate is drawn exactly as mark_svg draws it)."""
    base = mark_svg(plate=plate, node=tri, wire=wire, shape=shape, small=small, edge=edge, size=size)
    # reuse the plate + clip of mark_svg, swap the drawing inside the clipped group
    head, _, rest = base.partition('<g clip-path="url(#c)">')
    return head + '<g clip-path="url(#c)">' + player_group(tri, wire, small) + "</g></svg>"


# variants: name -> (svg kwargs). `small` twins are made for the plate variants used at 16 to 32 px.
VARIANTS = {
    "camelgraph-mark": dict(plate=GRAD),                                           # the logo: 5e on the dark gradient plate
    "camelgraph-mark-on-dark": dict(plate=None, shape="none"),                      # no plate, for dark backgrounds
    "camelgraph-mark-on-light": dict(plate=None, shape="none", node=PLATE, wire=BLUE_LIGHT),
    "camelgraph-mark-light": dict(plate=WHITE, node=PLATE, wire=BLUE_LIGHT, edge=EDGE),
    "camelgraph-mark-round": dict(plate=GRAD, shape="round"),
    "camelgraph-mark-mono-white": dict(plate=None, shape="none", node=WHITE, wire=WHITE),
    "camelgraph-mark-mono-black": dict(plate=None, shape="none", node=PLATE, wire=PLATE),
    "camelgraph-mark-mono-plate": dict(plate=PLATE, node=WHITE, wire=WHITE),       # ribbon button, white on black
    "camelgraph-mark-mono-plate-inverse": dict(plate=WHITE, node=PLATE, wire=PLATE, edge=EDGE),
    "camelgraph-mark-square": dict(plate=GRAD, shape="square"),                     # full-bleed, the OS rounds it
    "camelgraph-mark-maskable": dict(plate=GRAD, shape="square", scale=0.78),       # Android / PWA safe zone
}
SMALL_TWINS = ["camelgraph-mark", "camelgraph-mark-light", "camelgraph-mark-mono-plate", "camelgraph-mark-mono-plate-inverse",
               "camelgraph-mark-round", "camelgraph-mark-square", "camelgraph-mark-on-dark", "camelgraph-mark-on-light"]


# the Player mark on the same plates: name -> player_svg kwargs
PLAYER_VARIANTS = {
    "camelgraph-player": dict(plate=GRAD),
    "camelgraph-player-mono-plate": dict(plate=PLATE, tri=WHITE, wire=WHITE),          # the ribbon button, white on black
    "camelgraph-player-on-dark": dict(plate=None, shape="none"),
    "camelgraph-player-on-light": dict(plate=None, shape="none", tri=PLATE, wire=BLUE_LIGHT),
    "camelgraph-player-light": dict(plate=WHITE, tri=PLATE, wire=BLUE_LIGHT, edge=EDGE),
}
PLAYER_PLATED = ["camelgraph-player", "camelgraph-player-mono-plate", "camelgraph-player-light"]


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


def wordmark_path(cap_height: float, x: float, baseline: float) -> tuple[str, float]:
    """The name in pixel letters: (path data, width). `cap_height` is the height of the capital C; `baseline` the y of its foot."""
    cell = cap_height / pixel_font.CAP
    d = pixel_font.path_data("CamelGraph", cell, x, baseline - cap_height)
    return d, pixel_font.size_cells("CamelGraph")[0] * cell


def lockup_svg(share_tech: pathlib.Path | None, *, dark_background: bool, stacked: bool = False) -> str:
    """The mark with the name in pixel letters and a small 'by BIMCamel' under it (Share Tech, outlined)."""
    text = WHITE if dark_background else PLATE
    muted = "#9AA3AF" if dark_background else "#4B525C"
    # no plate: light node ends on a dark background, dark ones on a light background
    mark = mark_svg(size=120, plate=None, shape="none", node=WHITE if dark_background else PLATE,
                    wire=BLUE if dark_background else BLUE_LIGHT)
    if stacked:
        name, w = wordmark_path(48, 0, 0)
        by, w2 = (text_path(share_tech, "by BIMCamel", 24, 0, 0, 1.0) if share_tech else ("", 0))
        width = 400
        big = mark.replace('width="120" height="120"', 'width="150" height="150"', 1)
        inner = (f'<g transform="translate({(width - 150) / 2} 0)">{big}</g>'
                 f'<path transform="translate({(width - w) / 2} 238)" d="{name}" fill="{text}"/>')
        if by:
            inner += f'<path transform="translate({(width - w2) / 2} 292)" d="{by}" fill="{muted}"/>'
        return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {width} 310" width="{width}" height="310">{inner}</svg>'
    name, w = wordmark_path(45, 0, 0)
    by, _ = (text_path(share_tech, "by BIMCamel", 22, 0, 0, 1.0) if share_tech else ("", 0))
    width = 120 + 28 + w + 8
    inner = (f"{mark}"
             f'<path transform="translate(148 72)" d="{name}" fill="{text}"/>')
    if by:
        inner += f'<path transform="translate(150 106)" d="{by}" fill="{muted}"/>'
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
    # the cards are dark, so the mark is the plate-less light one: a black plate would nearly vanish on the navy
    return mark_svg(size=int(size), plate=None, shape="none").replace("<svg ", f'<svg x="{x}" y="{y}" ', 1)


def _text(x, y, s, size, fill, weight=400, anchor="start", spacing=0, family="Google Sans Flex"):
    from xml.sax.saxutils import escape
    return (f'<text x="{x}" y="{y}" fill="{fill}" font-family="{family}" font-size="{size}" font-weight="{weight}" '
            f'letter-spacing="{spacing}" text-anchor="{anchor}">{escape(s)}</text>')


TAGLINE = "Visual programming for Navisworks"
SUBLINE = "by BIMCamel · previously Dyncamelo"
URL = "bimcamel.com/plugins/dyncamelo"


def _pixel_name(x: float, baseline: float, cap: float, fill: str, anchor: str = "start") -> str:
    d, width = wordmark_path(cap, 0, 0)
    if anchor == "middle":
        x -= width / 2
    return f'<path transform="translate({x:g} {baseline - 0:g})" d="{d}" fill="{fill}"/>'


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
    s += _pixel_name(tx, ty + round(title * 0.28), round(title * 0.72), WHITE)
    s += _text(tx, ty + round(title * 0.28) + round(tag * 1.55), TAGLINE, tag, "#CBD5E1")
    s += _text(tx, ty + round(title * 0.28) + round(tag * 1.55) + round(sub * 1.8), SUBLINE, sub, "#94A3B8", 500)
    if footer:
        s += _text(pad, h - round(h * 0.08), URL, round(h * 0.04), "#94A3B8", 600)
    s += f'<rect x="0" y="{h - 8}" width="{w}" height="8" fill="{BLUE}"/>'
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}">{s}</svg>'


def social_square(n: int) -> str:
    mark = round(n * 0.34)
    s = _bg(n, n) + _mark_nested((n - mark) // 2, round(n * 0.17), mark)
    s += _pixel_name(n // 2, round(n * 0.64), round(n * 0.105 * 0.72), WHITE, "middle")
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


def mark_xaml() -> str:
    """The marks as WPF control templates, for the editor and the Player: the node ends take the control's Foreground, the
    wire its BorderBrush, so one drawing is light on a dark palette and dark on a light one (src/CamelGraph.UI/Themes/Logo.xaml).
    The Player mark also takes the Background (the gap where one wire passes under the other); the name is one Path in the
    Foreground, 120 x 19 cells (give it a height that is a multiple of 19 for crisp cells)."""
    def template(key: str, small: bool, note: str) -> str:
        g = _geometry(small)
        rings = "\n".join(
            f'        <Path Stroke="{{TemplateBinding BorderBrush}}" StrokeThickness="{g["ring_w"]}">'
            f'<Path.Data><EllipseGeometry Center="{cx},{cy}" RadiusX="{g["ring_r"]}" RadiusY="{g["ring_r"]}"/></Path.Data></Path>'
            for cx, cy in g["rings"]
        )
        return (
            f'    <!-- {note} -->\n'
            f'    <ControlTemplate x:Key="{key}" TargetType="{{x:Type ContentControl}}">\n'
            f'      <Viewbox Stretch="Uniform">\n'
            f'        <Canvas Width="100" Height="100" ClipToBounds="True">\n'
            f'        <Path Data="{g["node_left"]}" Fill="{{TemplateBinding Foreground}}"/>\n'
            f'        <Path Data="{g["node_right"]}" Fill="{{TemplateBinding Foreground}}"/>\n'
            f'        <Path Data="{g["wire"]}" Stroke="{{TemplateBinding BorderBrush}}" StrokeThickness="{g["wire_w"]}"'
            f' StrokeStartLineCap="Round" StrokeEndLineCap="Round"/>\n'
            f'{rings}\n'
            f'        </Canvas>\n'
            f'      </Viewbox>\n'
            f'    </ControlTemplate>\n')

    def player(key: str, small: bool, note: str) -> str:
        g = _player_geometry(small)
        w, gap = g["wire_w"], g["wire_w"] + 2 * (PLAYER_SMALL if small else PLAYER)["gap"]
        pl = PLAYER_SMALL if small else PLAYER
        upper = f"M{pl['x']:g} {pl['top']:g} L{pl['apex']:g} {pl['mid']:g}"
        caps = 'StrokeStartLineCap="Round" StrokeEndLineCap="Round"'
        rings = "\n".join(
            f'        <Path Stroke="{{TemplateBinding BorderBrush}}" StrokeThickness="{g["ring_w"]}">'
            f'<Path.Data><EllipseGeometry Center="{cx},{cy}" RadiusX="{g["ring_r"]}" RadiusY="{g["ring_r"]}"/></Path.Data></Path>'
            for cx, cy in g["rings"]
        )
        return (
            f'    <!-- {note} -->\n'
            f'    <ControlTemplate x:Key="{key}" TargetType="{{x:Type ContentControl}}">\n'
            f'      <Viewbox Stretch="Uniform">\n'
            f'        <Canvas Width="100" Height="100" ClipToBounds="True">\n'
            f'        <Path Data="{g["tri"]}" Stroke="{{TemplateBinding Foreground}}" StrokeThickness="{w}" {caps} StrokeLineJoin="Round"/>\n'
            f'        <Path Data="{g["bottom_cut"]}" Stroke="{{TemplateBinding Background}}" StrokeThickness="{gap}" {caps}/>\n'
            f'        <Path Data="{g["vert"]}" Stroke="{{TemplateBinding BorderBrush}}" StrokeThickness="{w}" {caps}/>\n'
            f'        <Path Data="{g["top_cut"]}" Stroke="{{TemplateBinding Background}}" StrokeThickness="{gap}" {caps}/>\n'
            f'        <Path Data="{upper}" Stroke="{{TemplateBinding Foreground}}" StrokeThickness="{w}" {caps}/>\n'
            f'{rings}\n'
            f'        </Canvas>\n'
            f'      </Viewbox>\n'
            f'    </ControlTemplate>\n')

    cells_w, cells_h = pixel_font.size_cells("CamelGraph")
    wordmark = (
        '    <!-- The name in pixel letters (the grid of the BIMCamel wordmark). Foreground = the letters. -->\n'
        '    <ControlTemplate x:Key="Dyc.Logo.Wordmark" TargetType="{x:Type ContentControl}">\n'
        '      <Viewbox Stretch="Uniform">\n'
        f'        <Canvas Width="{cells_w}" Height="{cells_h}">\n'
        f'        <Path Data="{pixel_font.path_data("CamelGraph", 1)}" Fill="{{TemplateBinding Foreground}}"/>\n'
        '        </Canvas>\n'
        '      </Viewbox>\n'
        '    </ControlTemplate>\n')
    return (
        '<!-- Generated by tools/logo/build_logo.py: do not edit by hand. -->\n'
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"\n'
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">\n'
        + template("Dyc.Logo.Mark", False, "The mark for 48 px and up. Use: ContentControl Foreground = node ends, BorderBrush = wire.")
        + "\n"
        + template("Dyc.Logo.MarkSmall", True, "The mark for 16 to 32 px: thicker wire, bigger sockets, taller node ends.")
        + "\n"
        + player("Dyc.Logo.Player", False, "The Player mark for 48 px and up. Foreground = the triangle, BorderBrush = the vertical wire and its rings, Background = the gap where one wire passes under the other (the surface colour).")
        + "\n"
        + player("Dyc.Logo.PlayerSmall", True, "The Player mark for 16 to 32 px.")
        + "\n"
        + wordmark
        + '</ResourceDictionary>\n')


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
    for name in ["camelgraph-mark-mono-white", "camelgraph-mark-mono-black"]:
        for px in (64, 128, 256, 512, 1024):
            jobs.append(dict(svg=str(svg_dir / f"{name}.svg"), out=str(png_dir / f"{name}-{px}.png"), width=px))
    # the marks without a plate: light on dark surfaces, dark on light ones (the small drawing up to 48 px, so chips stay legible)
    for name in ["camelgraph-mark-on-dark", "camelgraph-mark-on-light"]:
        for px in (16, 24, 32, 48, 64, 96, 128, 192, 256, 512, 1024):
            src = f"{name}-small" if px <= 48 else name
            jobs.append(dict(svg=str(svg_dir / f"{src}.svg"), out=str(png_dir / f"{name}-{px}.png"), width=px))
    for px in (48, 64):     # the small drawing, a little larger: for chips of 24 to 32 px at up to 200% scaling
        jobs.append(dict(svg=str(svg_dir / "camelgraph-mark-small.svg"), out=str(png_dir / f"camelgraph-mark-small-{px}.png"), width=px))
    jobs.append(dict(svg=str(svg_dir / "camelgraph-mark-maskable.svg"), out=str(png_dir / "camelgraph-mark-maskable-512.png"), width=512))
    jobs.append(dict(svg=str(svg_dir / "camelgraph-mark-maskable.svg"), out=str(png_dir / "camelgraph-mark-maskable-192.png"), width=192))

    # the Player mark: SVG, and PNG at the sizes it is used at (the small drawing up to 32 px on a plate, 48 px without one)
    for name, kw in PLAYER_VARIANTS.items():
        write(svg_dir / f"{name}.svg", player_svg(**kw))
        write(svg_dir / f"{name}-small.svg", player_svg(**{**kw, "small": True}))
        small_upto = 32 if name in PLAYER_PLATED else 48
        for px in (16, 24, 32, 48, 64, 96, 128, 256, 512):
            src = f"{name}-small" if px <= small_upto else name
            jobs.append(dict(svg=str(svg_dir / f"{src}.svg"), out=str(png_dir / f"{name}-{px}.png"), width=px))

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

    # the editor's templates and the wiki header's two marks
    write(ROOT / "src" / "CamelGraph.UI" / "Themes" / "Logo.xaml", mark_xaml())
    for tag in ("on-dark", "on-light"):
        write(ROOT / "tools" / "wiki" / "overrides" / "assets" / f"camelgraph-mark-{tag}.svg",
              (svg_dir / f"camelgraph-mark-{tag}.svg").read_text(encoding="utf-8"))

    # 3. icons: one .ico per use, each size drawn at that size (the small drawing up to 32 px)
    def ico(name: str, variant: str, sizes: list[int]) -> None:
        ico_dir.mkdir(parents=True, exist_ok=True)
        frames = [Image.open(png_dir / f"{variant}-{px}.png").convert("RGBA") for px in sizes]
        frames[-1].save(ico_dir / name, format="ICO", sizes=[f.size for f in frames], append_images=frames[:-1])

    ico("camelgraph.ico", "camelgraph-mark", [16, 24, 32, 48, 64, 128, 256])
    ico("camelgraph-mono.ico", "camelgraph-mark-mono-plate", [16, 24, 32, 48, 64, 128, 256])
    ico("favicon.ico", "camelgraph-mark", [16, 32, 48])

    # 4. copies the product uses: ribbon and About icons, installer, store, wiki favicon. One list, so nothing is copied by hand.
    publish = [
        ("png/camelgraph-mark-mono-plate-16.png", "src/CamelGraph.App/Resources/camelgraph_16.png"),
        ("png/camelgraph-mark-mono-plate-32.png", "src/CamelGraph.App/Resources/camelgraph_32.png"),
        ("png/camelgraph-player-mono-plate-16.png", "src/CamelGraph.App/Resources/player_16.png"),
        ("png/camelgraph-player-mono-plate-32.png", "src/CamelGraph.App/Resources/player_32.png"),
        ("png/camelgraph-mark-on-dark-96.png", "src/CamelGraph.App/Resources/camelgraph_about_96.png"),
        ("png/camelgraph-mark-mono-plate-16.png", "dist/CamelGraph.bundle/2024/Resources/camelgraph_16.png"),
        ("png/camelgraph-mark-mono-plate-32.png", "dist/CamelGraph.bundle/2024/Resources/camelgraph_32.png"),
        ("png/camelgraph-mark-on-dark-192.png", "src/CamelGraph.Installer/Resources/logo.png"),
        ("png/camelgraph-mark-on-dark-48.png", "src/CamelGraph.Installer/Resources/logo-small.png"),
        ("ico/camelgraph.ico", "src/CamelGraph.Installer/Resources/camelgraph.ico"),
        ("ico/camelgraph.ico", "appstore/assets/CamelGraph.ico"),
        ("png/camelgraph-mark-80.png", "appstore/assets/icon-80.png"),
        ("png/camelgraph-mark-120.png", "appstore/assets/icon-120.png"),
        ("png/camelgraph-mark-256.png", "appstore/assets/icon-256.png"),
        ("png/camelgraph-mark-256.png", "assets/camelgraph-logo.png"),
        ("png/camelgraph-mark-192.png", "tools/wiki/overrides/assets/favicon.png"),
    ]
    for src, dst in publish:
        target = ROOT / dst
        if target.parent.is_dir():
            target.write_bytes((OUT / src).read_bytes())

    # sanity: what was written
    for p in sorted(ico_dir.glob("*.ico")):
        print(p.relative_to(ROOT), sorted(Image.open(p).info.get("sizes", [])))
    print("files:", sum(1 for _ in OUT.rglob("*") if _.is_file()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
