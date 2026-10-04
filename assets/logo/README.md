# CamelGraph logo

The mark is two node ends, cut by the edge of the icon, each with a ring socket, and one wire between them. It is drawn
from a single geometry in `tools/logo/build_logo.py`; every file in this folder comes from that script, so change the
script, not the files.

| Use | File |
|---|---|
| The logo, anywhere there is room (README, website, slides) | `svg/camelgraph-mark.svg`, `png/camelgraph-mark-256.png` |
| 16 to 32 px (tab icons, small chips): thicker wire, bigger sockets | `png/camelgraph-mark-16.png`, `-24`, `-32`; `svg/camelgraph-mark-small.svg` |
| Windows icon (installer, shortcuts) | `ico/camelgraph.ico` (16, 24, 32, 48, 64, 128, 256) |
| Browser tab | `ico/favicon.ico`, `png/camelgraph-mark-32.png` |
| Navisworks ribbon button (white on black, like the Player button) | `png/camelgraph-mark-mono-plate-16.png`, `-32.png`, `ico/camelgraph-mono.ico` |
| Light backgrounds (documents, white pages) | `svg/camelgraph-mark-light.svg`, `svg/camelgraph-mark-on-light.svg` (no plate) |
| Dark backgrounds, no plate | `svg/camelgraph-mark-on-dark.svg` |
| One colour only (print, stamps, embossing) | `svg/camelgraph-mark-mono-white.svg`, `svg/camelgraph-mark-mono-black.svg` |
| Round avatars and badges | `svg/camelgraph-mark-round.svg`, `social/avatar-round-800.png` |
| iOS and Android home-screen icons | `png/camelgraph-mark-square-180.png` (the OS rounds it), `png/camelgraph-mark-maskable-192.png` and `-512.png` |
| Autodesk App Store | `png/camelgraph-mark-80.png`, `-120.png`, `-256.png` |
| Name and mark together | `svg/camelgraph-lockup-on-dark.svg`, `-on-light.svg`; stacked: `camelgraph-lockup-stacked-on-dark.svg`, `-on-light.svg` |
| Social posts and previews | `social/og-1200x630.png`, `github-social-1280x640.png`, `linkedin-banner-1584x396.png`, `x-header-1500x500.png`, `post-square-1080.png` |

## Colours

| | |
|---|---|
| Plate | `#0D0E11` (the editor's background) |
| Node ends | `#FFFFFF` (on a light plate `#0D0E11`) |
| Wire and sockets | `#2E9BFF` on dark, `#1A73C5` on light (the editor's primary blue) |
| Social background | `#0A1020` to `#15233F` with a faint grid |

## Rules of thumb

* Keep clear space around the plate of at least a quarter of its width.
* Below 48 px use the `-small` drawing; do not scale the standard one down.
* Do not recolour the wire, stretch the mark, add a shadow or rotate it. Do not put the plate on a background of nearly the same dark.
* The name is set in Share Tech (SIL Open Font Licence 1.1), the face bimcamel.com uses for headings. The lock-up files contain
  its outlines, not the font. The social images use Google Sans Flex (SIL OFL), also from bimcamel.com.
* "CamelGraph" is the previous name. The mark replaced the old camel-on-a-plate logo everywhere in October 2026.

## Rebuilding

```
cd tools/logo && npm ci
python tools/logo/build_logo.py --fonts <the bimcamel.com fonts folder>
```
