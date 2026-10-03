#!/usr/bin/env python3
"""Copies the wiki pictures the pages use from a folder of drawn pictures into docs/images.

The Windows CI job draws every wiki scene (tests/Dyncamelo.UI.Tests/Wiki) into the `wiki` folder of the `editor-screenshots`
artifact: wiki/<id>.png for the dark theme, wiki/<id>-light.png for the light one, and wiki/_report.txt. Download and unzip the
artifact, then:

    python tools/wiki_pictures.py path/to/editor-screenshots/wiki

Only the pictures a page under docs/wiki-src mentions (as wiki-<name>.png) are copied, each with its -light variant when the
folder has one. They are saved as 256-colour PNGs (the editor's flat colours lose nothing you can see; a picture is about a third
of its drawn size), so the repository does not grow by 15 MB each time the pictures are refreshed.

Needs Pillow (tools/wiki/requirements.txt). `--check` only lists what would be copied.
"""
import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PAGES = ROOT / "docs" / "wiki-src"
IMAGES = ROOT / "docs" / "images"
REFERENCE = re.compile(r"\bwiki-[a-z0-9-]+?\.png\b")


def wanted_names():
    names = set()
    for page in PAGES.rglob("*.md"):
        for match in REFERENCE.findall(page.read_text(encoding="utf-8")):
            if not match.endswith("-light.png"):
                names.add(match)
    return sorted(names)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("source", help="the folder with the drawn pictures (the artifact's `wiki` folder)")
    parser.add_argument("--check", action="store_true", help="list what would be copied, change nothing")
    args = parser.parse_args()

    source = Path(args.source)
    if not source.is_dir():
        sys.exit("not a folder: %s" % source)

    if not args.check:
        try:
            from PIL import Image
        except ImportError:
            sys.exit("Pillow is missing: pip install -r tools/wiki/requirements.txt")

    copied = 0
    missing = []
    before = after = 0
    for name in wanted_names():
        for variant in (name, name[:-4] + "-light.png"):
            drawn = source / variant
            if not drawn.is_file():
                if variant == name:
                    missing.append(name)
                continue
            target = IMAGES / variant
            before += drawn.stat().st_size
            if args.check:
                print("would copy", variant)
                continue
            picture = Image.open(drawn).convert("RGB")
            picture.quantize(256, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).save(target, "PNG", optimize=True)
            after += target.stat().st_size
            copied += 1

    if not args.check:
        print("copied %d pictures into docs/images (%.1f MB drawn, %.1f MB saved)" % (copied, before / 1e6, after / 1e6))
    if missing:
        print("not drawn: " + ", ".join(missing))
        sys.exit(1)


if __name__ == "__main__":
    main()
