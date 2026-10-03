#!/usr/bin/env python3
"""Builds the Dyncamelo wiki: a static site for bimcamel.com, made with MkDocs and the Material theme.

What it does
  1. Puts all pages in a staging folder (default build/wiki-docs):
       docs/wiki-src/*.md, howto/*.md   the pages written by hand (YAML front matter allowed: title, order, summary)
       docs/wiki-src/graphs/*.dyc       graph files, offered for download
       docs/TROUBLESHOOTING.md, docs/RECIPES.md, docs/EXTENDING.md
                                        the repository's own documents, with their links turned into links of the site
       CHANGELOG.md                     becomes the "What's new" page
       docs/dyncamelo-nodes.json        becomes the node reference (nodes/*.md, one page per category group)
       docs/images/<id>.png             the pictures the pages use; <id>-light.png is the light-theme version when it exists
  2. Writes build/wiki-docs.mkdocs.yml (the navigation from docs/wiki-src/nav.yml, the addresses from tools/wiki/site.yml).
  3. Runs `mkdocs build --strict` with tools/wiki/mkdocs.yml, into build/wiki-site.

Usage
  python tools/build_wiki.py                         build; any warning fails the build
  python tools/build_wiki.py --serve                 build, then serve with live reload on http://127.0.0.1:8000
  python tools/build_wiki.py --allow-missing-images  (drafts) a missing picture is a warning and shows a note in its place
  python tools/build_wiki.py --allow-missing-files   (drafts) a link to a graph file that is not there yet is shown as text
  python tools/build_wiki.py --draft                 both of the above
  python tools/build_wiki.py --stage-only            only write the staging folder and the MkDocs file
  python tools/build_wiki.py --out DIR               write the site to DIR instead of build/wiki-site

Needs the packages in tools/wiki/requirements.txt: pip install -r tools/wiki/requirements.txt
"""

import argparse
import html
import json
import os
import posixpath
import re
import subprocess
import sys
import time
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover - the message is the point
    sys.exit("build_wiki.py needs the packages in tools/wiki/requirements.txt: pip install -r tools/wiki/requirements.txt")

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "docs" / "wiki-src"
NAV_FILE = SRC / "nav.yml"
CATALOGUE = ROOT / "docs" / "dyncamelo-nodes.json"
IMAGES = ROOT / "docs" / "images"
TOOLS = ROOT / "tools" / "wiki"
MKDOCS_BASE = TOOLS / "mkdocs.yml"
SITE_FILE = TOOLS / "site.yml"
BUILD = ROOT / "build"
STAGE_DEFAULT = BUILD / "wiki-docs"
SITE_DEFAULT = BUILD / "wiki-site"
STAGE_MARKER = ".wiki-stage"

# Pages whose text lives in the repository's own documents, so there is one copy of it: the wiki page is that document with its
# links turned into links of the site (or, for files the site does not have, into the file on GitHub).
REPO_PAGES = {
    "troubleshooting": "docs/TROUBLESHOOTING.md",
    "recipes": "docs/RECIPES.md",
    "extending": "docs/EXTENDING.md",
}

# Where a link from a repository document goes on the site.
LINK_MAP = {
    "GETTING_STARTED.md": "first-steps.md",
    "TROUBLESHOOTING.md": "troubleshooting.md",
    "RECIPES.md": "recipes.md",
    "EXTENDING.md": "extending.md",
    "NODE_CATALOG.md": "nodes/index.md",
    "NODE_LIBRARY.md": "nodes/index.md",
    "UI_GUIDE.md": "canvas-and-nodes.md",
    "SECURITY.md": "privacy-and-safety.md",
    "PRIVACY.md": "privacy-and-safety.md",
    "samples/README.md": "samples.md",
}

CHANGELOG_KEY = "whats-new"
NODES_INDEX_KEY = "nodes/index"
SITE_KEYS = ("github_url", "app_store_url", "bimcamel_url", "repo_url")


class BuildError(Exception):
    pass


# ------------------------------------------------------------------------------------------------------------------------
# Small helpers
# ------------------------------------------------------------------------------------------------------------------------

def read_text(path):
    return Path(path).read_text(encoding="utf-8").replace("\r\n", "\n")


def read_version():
    props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    match = re.search(r"<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>", props)
    return match.group(1) if match else "0.0.0"


def slug(text):
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def md_text(text):
    """Plain text made safe for Markdown: nothing in it is taken for formatting, a link, HTML or a table border."""
    out = []
    for ch in str(text):
        if ch == "&":
            out.append("&amp;")
        elif ch == "<":
            out.append("&lt;")
        elif ch == ">":
            out.append("&gt;")
        elif ch == "|":
            out.append("&#124;")
        elif ch == ":":
            out.append("&#58;")
        elif ch in "\\`*_{}[]#+!":
            out.append("\\" + ch)
        else:
            out.append(ch)
    result = "".join(out)
    result = re.sub(r"^(\d+)([.)])", r"\1\\\2", result)   # not a numbered list
    result = re.sub(r"^-", r"\\-", result)                  # not a bullet
    return result


def code(text, nowrap=False):
    return '<code%s>%s</code>' % (' class="nowrap"' if nowrap else "", md_text(text))


def chip(text, extra=""):
    return '<span class="chip%s">%s</span>' % (extra, html.escape(text))


def plain(text):
    """A line of Markdown as plain text (for labels and descriptions)."""
    text = re.sub(r"!\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"[`*]", "", text)
    text = re.sub(r"<[^>]+>", "", text)
    return re.sub(r"\s+", " ", text).strip()


def summary_of(body, limit=170):
    """The first paragraph after the page title, as plain text (for the page description)."""
    lines = body.split("\n")
    i = 0
    while i < len(lines) and not lines[i].startswith("# "):
        i += 1
    para = []
    for line in lines[i + 1:]:
        stripped = line.strip()
        if not stripped:
            if para:
                break
            continue
        if not para and stripped.startswith(("#", "|", "```", "~~~", "-", "*", ">", "!", "<", "1.", "???", "===")):
            continue
        if para and stripped.startswith(("#", "|", "```", "~~~")):
            break
        para.append(stripped)
    text = plain(" ".join(para))
    if len(text) > limit:
        text = text[:limit].rsplit(" ", 1)[0].rstrip(",;:") + "..."
    return text


def split_front_matter(text, where):
    """(meta, body) of a page that may start with a YAML front matter block."""
    match = re.match(r"\A---[ \t]*\n(.*?)\n---[ \t]*(?:\n|\Z)", text, re.S)
    if not match:
        return {}, text
    try:
        meta = yaml.safe_load(match.group(1)) or {}
    except yaml.YAMLError as error:
        raise BuildError("%s: the front matter is not valid YAML (%s)" % (where, str(error).splitlines()[0]))
    if not isinstance(meta, dict):
        raise BuildError("%s: the front matter must be a list of 'key: value' lines" % where)
    return meta, text[match.end():].lstrip("\n")


def title_of(body, where):
    first = next((line for line in body.split("\n") if line.strip()), "")
    if not first.startswith("# ") or not first[2:].strip():
        raise BuildError("%s must start with a '# Title' line (after the front matter, if there is one)" % where)
    return first[2:].strip()


INLINE_CODE = re.compile(r"(?<!`)(`+)(?!`)(.+?)(?<!`)\1(?!`)")
FENCE = re.compile(r"^\s*(`{3,}|~{3,})")
LINK_OR_PICTURE = re.compile(r'(!?)\[((?:[^\]\\]|\\.)*)\]\(\s*([^)\s]*)((?:\s+"[^"]*")?)\s*\)(\{[^}\n]*\})?')


def closes_fence(match, fence, line):
    return bool(match) and match.group(1)[0] == fence[0] and len(match.group(1)) >= len(fence) and line.strip() == match.group(1)


def outside_code(line, function):
    """Applies `function` to a line of a page with its `inline code` hidden, so a link or a picture is found even when its
    text contains code."""
    spans = []

    def hide(found):
        spans.append(found.group(0))
        return "\x00%d\x00" % (len(spans) - 1)

    result = function(INLINE_CODE.sub(hide, line))
    return re.sub(r"\x00(\d+)\x00", lambda found: spans[int(found.group(1))], result)


# ------------------------------------------------------------------------------------------------------------------------
# The staging folder
# ------------------------------------------------------------------------------------------------------------------------

class Page:
    def __init__(self, key, body, title, meta=None, edit_path=None, description=None):
        self.key = key
        self.body = body.rstrip("\n") + "\n"
        self.title = title
        self.meta = dict(meta or {})
        self.edit_path = edit_path
        self.description = description

    @property
    def directory(self):
        return posixpath.dirname(self.key)

    def render(self):
        meta = {"title": self.title}
        extra = dict(self.meta)
        description = extra.pop("description", None) or self.description
        if description:
            # the theme writes it into <meta name="description" content="...">, so quotes and & must be escaped
            meta["description"] = html.escape(str(description), quote=True)
        if self.edit_path:
            meta["edit_path"] = self.edit_path
        meta.update(extra)
        front = yaml.safe_dump(meta, allow_unicode=True, sort_keys=False, width=10000, default_flow_style=False)
        return "---\n" + front + "---\n\n" + self.body


class Stage:
    """Everything the build puts into the staging folder: pages, pictures, graph files."""

    def __init__(self, allow_missing_images=False, allow_missing_files=False):
        self.allow_missing_images = allow_missing_images
        self.allow_missing_files = allow_missing_files
        self.downloads = {path.relative_to(SRC).as_posix() for path in SRC.rglob("*")
                          if path.is_file() and path.suffix.lower() != ".md" and path != NAV_FILE and path.name != "README"}
        self.problems = []
        self.warnings = []
        self.pages = {}           # key -> Page
        self.files = {}           # relative path -> bytes or Path
        self.images_used = set()

    def image_markup(self, alt, src, title, attrs, page, standalone):
        """Markdown for one picture. A picture that has a light version is shown in its own theme."""
        if re.match(r"^(https?:|data:|//)", src):
            return None
        name = posixpath.basename(src.split("#")[0])
        if not (IMAGES / name).is_file():
            message = "%s uses the picture '%s', which is not in docs/images" % (page.key, name)
            if self.allow_missing_images:
                message += " (a note is shown in its place)"
                if message not in self.warnings:
                    self.warnings.append(message)
                note = "**[picture to come: %s]**" % name
                return ("\n" + note + "\n") if standalone else note
            if message not in self.problems:
                self.problems.append(message)
            return None
        self.images_used.add(name)
        stem, dot, suffix = name.rpartition(".")
        light_name = "%s-light.%s" % (stem, suffix)
        has_light = bool(dot) and not stem.endswith("-light") and (IMAGES / light_name).is_file()
        if has_light:
            self.images_used.add(light_name)

        def rel(file_name):
            return posixpath.relpath("images/" + file_name, page.directory or ".")

        alt_plain = alt.replace("`", "").replace("*", "").replace("[", "").replace("]", "")
        extra = (attrs or "").strip()
        extra = extra[1:-1].strip() if extra.startswith("{") and extra.endswith("}") else ""
        if "loading" not in extra:
            extra = (extra + " loading=lazy").strip()
        tail = "{ %s }" % extra
        if has_light:
            pictures = ["![%s](%s#only-dark)%s" % (alt_plain, rel(name), tail),
                        "![%s](%s#only-light)%s" % (alt_plain, rel(light_name), tail)]
        else:
            pictures = ["![%s](%s)%s" % (alt_plain, rel(name), tail)]
        caption = re.sub(r"[`*]", "", alt.strip() or (title or "").strip())
        if standalone and caption:
            return "\n".join(['<figure markdown="span">'] + ["  " + p for p in pictures]
                             + ["  <figcaption>%s</figcaption>" % caption, "</figure>"])
        return "".join(pictures)

    def download_link(self, label, target, page, original):
        """A link to a file that is not a page (a graph to download): it must exist. Returns the text to use, or None to keep it."""
        if re.match(r"^(https?:|mailto:|#|/)", target):
            return None
        path = target.split("#")[0].split("?")[0]
        suffix = posixpath.splitext(path)[1].lower()
        if not suffix or suffix in (".md", ".html"):
            return None
        resolved = posixpath.normpath(posixpath.join(page.directory, path))
        if resolved in self.downloads:
            return None
        if resolved.startswith("images/") and (IMAGES / posixpath.basename(resolved)).is_file():
            self.images_used.add(posixpath.basename(resolved))
            return None
        message = "%s links to the file '%s', which is not in docs/wiki-src" % (page.key, resolved)
        if self.allow_missing_files:
            message += " (the link is shown as plain text)"
            if message not in self.warnings:
                self.warnings.append(message)
            return "%s (file to come)" % label
        if message not in self.problems:
            self.problems.append(message)
        return None

    def process(self, text, page, link_function=None):
        """Turns the pictures of a page into figures and, if asked, rewrites its links (not in code)."""

        def one(match, standalone=False):
            bang, label, target, title_part, attrs = match.groups()
            if bang:
                title = title_part.strip().strip('"') if title_part else ""
                made = self.image_markup(label, target, title, attrs, page, standalone)
                return made if made is not None else match.group(0)
            if link_function:
                new_target = link_function(target)
                if new_target is not None and new_target != target:
                    return "[%s](%s%s)%s" % (label, new_target, title_part, attrs or "")
                return match.group(0)
            replaced = self.download_link(label, target, page, match.group(0))
            return replaced if replaced is not None else match.group(0)

        out = []
        fence = None
        for line in text.split("\n"):
            match = FENCE.match(line)
            if fence:
                out.append(line)
                if closes_fence(match, fence, line):
                    fence = None
                continue
            if match:
                fence = match.group(1)
                out.append(line)
                continue
            whole = LINK_OR_PICTURE.fullmatch(line.strip())
            indent = re.match(r"\s*", line).group(0)
            # a picture alone on its line becomes a figure (not when it is a loose continuation of a list item)
            if whole and whole.group(1) and (indent == "" or len(indent.expandtabs(4)) >= 4):
                made = one(whole, standalone=True)
                if "\n" in made:
                    out.append("")
                    out.extend(indent + b for b in made.strip("\n").split("\n"))
                    out.append("")
                    continue
            out.append(outside_code(line, lambda fragment: LINK_OR_PICTURE.sub(one, fragment)))
        return "\n".join(out)


def load_site(problems):
    try:
        site = yaml.safe_load(SITE_FILE.read_text(encoding="utf-8")) or {}
    except (OSError, yaml.YAMLError) as error:
        problems.append("tools/wiki/site.yml cannot be read: %s" % error)
        return {}
    for key in SITE_KEYS:
        value = site.get(key)
        if key == "app_store_url" and (value is None or value == ""):
            site[key] = ""      # empty: the Autodesk App Store copy is "coming soon", greyed out and without a link
            continue
        if not isinstance(value, str) or not value.startswith("https://"):
            problems.append("tools/wiki/site.yml needs '%s' with an https:// address%s"
                            % (key, " (or an empty one for 'coming soon')" if key == "app_store_url" else ""))
    return site


def load_nav(problems):
    try:
        nav = yaml.safe_load(NAV_FILE.read_text(encoding="utf-8"))
    except (OSError, yaml.YAMLError) as error:
        problems.append("docs/wiki-src/nav.yml cannot be read: %s" % error)
        return []
    if not isinstance(nav, list) or not nav:
        problems.append("docs/wiki-src/nav.yml must be a list of sections")
        return []
    sections = []
    for entry in nav:
        if not isinstance(entry, dict) or "section" not in entry or not (("pages" in entry) ^ ("glob" in entry)):
            problems.append("docs/wiki-src/nav.yml: each entry needs 'section' and either 'pages' or 'glob': %r" % (entry,))
            continue
        bad = [p for p in entry.get("pages", []) if not isinstance(p, dict) or "key" not in p or "label" not in p]
        if bad:
            problems.append("docs/wiki-src/nav.yml: each page needs 'key' and 'label': %r" % (bad[0],))
            continue
        sections.append(entry)
    return sections


# ------------------------------------------------------------------------------------------------------------------------
# Hand-written pages, repository documents, changelog
# ------------------------------------------------------------------------------------------------------------------------

def written_page(stage, key, path):
    where = path.relative_to(ROOT).as_posix()
    meta, body = split_front_matter(read_text(path), where)
    heading = title_of(body, where)
    title = plain(str(meta.pop("title", None) or heading))
    summary = meta.pop("summary", None)
    meta.pop("order", None)
    if summary and "description" not in meta:
        meta["description"] = str(summary).strip()
    page = Page(key, body, title, meta, edit_path=where, description=summary_of(body))
    page.body = stage.process(page.body, page)
    return page


def repo_link(target, key, site):
    """Where a link in a repository document goes on the site."""
    if re.match(r"^(https?:|mailto:|#)", target):
        return None
    path, _, anchor = target.partition("#")
    resolved = posixpath.normpath(posixpath.join(posixpath.dirname(REPO_PAGES[key]), path))
    for name, page in LINK_MAP.items():
        if resolved in ("docs/" + name, name):
            # the headings of the three documents get the same ids as on GitHub, so an anchor stays valid between them
            keep = page == key + ".md" or page[:-3] in REPO_PAGES
            return page + (("#" + anchor) if anchor and keep else "")
    return "%s/blob/main/%s%s" % (site["repo_url"].rstrip("/"), resolved, ("#" + anchor) if anchor else "")


def repo_page(stage, key, site):
    rel = REPO_PAGES[key]
    meta, body = split_front_matter(read_text(ROOT / rel), rel)
    heading = title_of(body, rel)
    page = Page(key, body, plain(heading), meta, edit_path=rel, description=summary_of(body))
    page.body = stage.process(page.body, page, lambda target: repo_link(target, key, site))
    return page


def changelog_page(stage, site):
    lines = read_text(ROOT / "CHANGELOG.md").split("\n")
    # drop the file's own '# Changelog' line and its intro paragraphs about how it was made; keep from the first '## ' on
    start = next((i for i, line in enumerate(lines) if line.startswith("## ")), 0)
    intro = ("# What's new\n\nEvery release of Dyncamelo and what changed in it, newest first. "
             "The newest section, *Unreleased*, lists what is already in the source and not yet in a numbered release.\n\n")
    page = Page(CHANGELOG_KEY, intro + "\n".join(lines[start:]), "What's new",
                description="Every release of Dyncamelo and what changed in it.")
    base = site["repo_url"].rstrip("/") + "/blob/main/"

    def to_github(target):
        # the changelog links to files of the repository; on the site those are the repository's pages
        if re.match(r"^(https?:|mailto:|#)", target):
            return None
        return base + posixpath.normpath(target)

    page.body = stage.process(page.body, page, to_github)
    return page


# ------------------------------------------------------------------------------------------------------------------------
# Node reference
# ------------------------------------------------------------------------------------------------------------------------

def node_group(category):
    parts = category.split(".")
    if parts[0] == "Navisworks" and len(parts) > 1:
        return "Navisworks." + parts[1]
    return parts[0]


def group_title(group):
    return group.replace("Navisworks.", "Navisworks: ") if group.startswith("Navisworks.") else group


def node_anchor(name):
    return "node-" + slug(name)


def plural(count, word):
    return "%d %s%s" % (count, word, "" if count == 1 else "s")


def render_node(node):
    anchor = node_anchor(node["name"])
    out = ["### %s { #%s .node }" % (md_text(node["name"]), anchor), "", md_text(node["description"]), ""]
    if node.get("interactive"):
        out += ['!!! note "Interactive node: it has its own controls on the canvas."', ""]
    if node.get("inputs"):
        out += ['<p class="node-sub">Inputs</p>', "",
                "| Input | Type | Default | What it does |", "|---|---|---|---|"]
        for port in node["inputs"]:
            flags = ""
            if port.get("multiInput"):
                flags += " " + chip("any number of wires")
            if "default" not in port:
                flags += " " + chip("required", " chip--required")
                default = "&mdash;"
            else:
                default = code(port["default"]) if port["default"] != "" else "*empty*"
            out.append("| %s%s | %s | %s | %s |" % (code(port["name"], True), flags, code(port["type"], True), default,
                                                    md_text(port.get("description", ""))))
        out.append("")
    else:
        out += ["*No inputs.*", ""]
    if node.get("outputs"):
        described = any(port.get("description") for port in node["outputs"])
        out += ['<p class="node-sub">Outputs</p>', ""]
        out += ["| Output | Type | What it gives |", "|---|---|---|"] if described else ["| Output | Type |", "|---|---|"]
        for port in node["outputs"]:
            row = "| %s | %s |" % (code(port["name"], True), code(port["type"], True))
            if described:
                row += " %s |" % md_text(port.get("description", ""))
            out.append(row)
        out.append("")
    else:
        out += ["*No outputs.*", ""]
    if node.get("returns"):
        out += ["Returns: %s" % md_text(node["returns"]), ""]
    tags = node.get("tags") or []
    if tags:
        out += ["Tags: " + " ".join(chip(tag) for tag in tags), ""]
    return "\n".join(out)


def node_pages(catalogue):
    """The index page and the pages of the groups of the node reference: (index, [(group, page, count)])."""
    nodes = catalogue["nodes"]
    groups = {}
    for node in nodes:
        groups.setdefault(node_group(node["category"]), []).append(node)
    order = sorted(groups, key=lambda g: (g.startswith("Navisworks"), g.lower()))

    pages = []
    seen_anchors = {}
    for group in order:
        items = sorted(groups[group], key=lambda n: (n["category"].lower(), n["name"].lower()))
        body = ["# %s" % md_text(group_title(group)), "",
                "%s. Types ending in <code>[]</code> are lists. An input with a default is optional."
                % plural(len(items), "node").capitalize(), ""]
        current = None
        for node in items:
            anchor = node_anchor(node["name"])
            if anchor in seen_anchors:
                raise BuildError("two nodes make the same anchor '%s': %s and %s" % (anchor, seen_anchors[anchor], node["name"]))
            seen_anchors[anchor] = node["name"]
            if node["category"] != current:
                current = node["category"]
                if current == group:
                    body += ["## Nodes { #%s-nodes }" % slug(group), ""]
                else:
                    heading = current[len(group) + 1:] if current.startswith(group + ".") else current
                    body += ["## %s { #%s }" % (md_text(heading), slug(current)), ""]
            body.append(render_node(node))
        page = Page("nodes/" + slug(group), "\n".join(body), group_title(group),
                    description="%s: inputs, outputs and what each does." % plural(len(items), "node").capitalize())
        pages.append((group, page, len(items)))

    # the index: category cards and a table of all nodes
    body = ["# Node reference", "",
            "Every node in the library, with its inputs and outputs: %s in %s. Pick a category below, or type a node's name in the search box at the top."
            % (plural(len(nodes), "node"), plural(len(order), "category group")), "",
            "## Categories", "", '<div class="grid cards" markdown>', ""]
    for group, page, count in pages:
        names = sorted((n["name"] for n in groups[group]), key=str.lower)[:3]
        body += ["- **[%s](%s.md)**" % (md_text(group_title(group)), page.key.split("/", 1)[1]), "",
                 "    %s" % plural(count, "node"), "",
                 "    " + ", ".join(code(n) for n in names) + ", ...", ""]
    body += ["</div>", "", "## All nodes { data-search-exclude }", "",
             "| Node | Category | What it does |", "|---|---|---|"]
    page_of = {group: page for group, page, _ in pages}
    for node in sorted(nodes, key=lambda n: n["name"].lower()):
        summary = node["description"]
        cut = summary.find(". ")
        short = summary if cut < 0 else summary[:cut + 1]
        target = "%s.md#%s" % (page_of[node_group(node["category"])].key.split("/", 1)[1], node_anchor(node["name"]))
        body.append("| [%s](%s) | %s | %s |" % (code(node["name"], True), target, md_text(node["category"]), md_text(short)))
    index = Page(NODES_INDEX_KEY, "\n".join(body), "Node reference",
                 description="Every node with its inputs and outputs, by category.")
    return index, pages


def node_nav(pages):
    """Navigation entries below 'All nodes': the general groups, then a 'Navisworks' section."""
    entries = [{group_title(group): page.key + ".md"} for group, page, _ in pages if not group.startswith("Navisworks.")]
    navisworks = [{group_title(group).replace("Navisworks: ", ""): page.key + ".md"}
                  for group, page, _ in pages if group.startswith("Navisworks.")]
    if navisworks:
        entries.append({"Navisworks": navisworks})
    return entries


# ------------------------------------------------------------------------------------------------------------------------
# Putting it together
# ------------------------------------------------------------------------------------------------------------------------

def build_stage(allow_missing_images, allow_missing_files):
    stage = Stage(allow_missing_images, allow_missing_files)
    problems = stage.problems
    site = load_site(problems)
    sections = load_nav(problems)
    if problems:
        return stage, site, []

    catalogue = json.loads(CATALOGUE.read_text(encoding="utf-8"))
    sources = {}
    for path in sorted(SRC.rglob("*.md")):
        key = path.relative_to(SRC).with_suffix("").as_posix()
        if key != "README":
            sources[key] = path

    listed = set()
    nav = []
    seen_nodes = False

    def add_page(key, label):
        """Reads the page of a nav.yml key into the stage. Returns the Page, 'nodes' for the node reference, or None."""
        if key in listed:
            problems.append("docs/wiki-src/nav.yml lists '%s' twice" % key)
            return None
        listed.add(key)
        try:
            if key in REPO_PAGES:
                if key in sources:
                    problems.append("docs/wiki-src/%s.md must not exist: the page is built from %s" % (key, REPO_PAGES[key]))
                    return None
                page = repo_page(stage, key, site)
            elif key == CHANGELOG_KEY:
                page = changelog_page(stage, site)
            elif key == NODES_INDEX_KEY:
                return "nodes"
            elif key.startswith("nodes/"):
                problems.append("docs/wiki-src/nav.yml lists '%s': the build makes the node pages, list only nodes/index" % key)
                return None
            elif key in sources:
                page = written_page(stage, key, sources[key])
            else:
                problems.append("missing page: docs/wiki-src/%s.md (listed in nav.yml as '%s')" % (key, label))
                return None
        except BuildError as error:
            problems.append(str(error))
            return None
        stage.pages[key] = page
        return page

    for section in sections:
        items = []
        if "glob" in section:
            found = []
            for path in sorted(SRC.glob(section["glob"])):
                key = path.relative_to(SRC).with_suffix("").as_posix()
                if path.suffix != ".md" or key == "README":
                    continue
                where = path.relative_to(ROOT).as_posix()
                try:
                    meta, _ = split_front_matter(read_text(path), where)
                except BuildError as error:
                    problems.append(str(error))
                    listed.add(key)
                    continue
                order = meta.get("order", 1000)
                if isinstance(order, bool) or not isinstance(order, (int, float)):
                    problems.append("%s: 'order' must be a number" % where)
                    listed.add(key)
                    continue
                found.append((order, path.name, key))
            for _, _, key in sorted(found):
                page = add_page(key, key)
                if isinstance(page, Page):
                    items.append({page.title: key + ".md"})
            if not found:
                print("wiki: note: the section '%s' (glob %s) matches no file yet, so it is left out"
                      % (section["section"], section["glob"]), file=sys.stderr)
        else:
            for entry in section["pages"]:
                result = add_page(entry["key"], entry["label"])
                if result == "nodes":
                    seen_nodes = True
                    index, group_pages = node_pages(catalogue)
                    stage.pages[index.key] = index
                    for _, page, _ in group_pages:
                        stage.pages[page.key] = page
                    items.append({entry["label"]: index.key + ".md"})
                    items.extend(node_nav(group_pages))
                elif result is not None:
                    items.append({entry["label"]: entry["key"] + ".md"})
        if items:
            nav.append({section["section"]: items})

    for key in sorted(set(sources) - listed):
        problems.append("docs/wiki-src/%s.md is not listed in nav.yml (and no 'glob' section matches it)" % key)
    if not seen_nodes:
        problems.append("docs/wiki-src/nav.yml must list 'nodes/index' (the node reference)")

    for key, page in stage.pages.items():
        stage.files[key + ".md"] = page.render().encode("utf-8")
    for name in sorted(stage.images_used):
        stage.files["images/" + name] = IMAGES / name
    # other files next to the pages (graphs to download, ...)
    for path in sorted(SRC.rglob("*")):
        if path.is_file() and path.suffix.lower() != ".md" and path != NAV_FILE and path.name != "README":
            stage.files[path.relative_to(SRC).as_posix()] = path
    return stage, site, nav


def mkdocs_config(site, nav, stage_dir, site_dir):
    """The small file that inherits tools/wiki/mkdocs.yml and adds what changes from build to build."""
    def posix(path):
        return Path(path).resolve().as_posix()

    config = {
        "INHERIT": posix(MKDOCS_BASE),
        "docs_dir": posix(stage_dir),
        "site_dir": posix(site_dir),
        "hooks": [posix(TOOLS / "hooks.py")],
        "theme": {"custom_dir": posix(TOOLS / "overrides")},
        "copyright": "Dyncamelo v%s by BIMCamel. Licensed under the PolyForm Noncommercial License 1.0.0." % read_version(),
        "extra": {key: site[key] for key in SITE_KEYS},
        "nav": nav,
    }
    return ("# Written by tools/build_wiki.py. Do not edit: the next build replaces it.\n"
            + yaml.safe_dump(config, allow_unicode=True, sort_keys=False, width=10000))


# ------------------------------------------------------------------------------------------------------------------------
# Writing files safely
# ------------------------------------------------------------------------------------------------------------------------

def ensure_not_dangerous(path, what):
    path = path.resolve()
    if path == ROOT or path in ROOT.parents or path == Path(path.anchor):
        raise BuildError("refusing to use %s as the %s folder" % (path, what))


def sync_tree(dest, files):
    """Makes `dest` hold exactly `files` (relative path -> bytes or a source Path), touching only what changed."""
    ensure_not_dangerous(dest, "staging")
    if dest.exists() and any(dest.iterdir()) and not (dest / STAGE_MARKER).exists():
        raise BuildError("%s is not empty and was not made by this build; choose another --stage folder" % dest)
    dest.mkdir(parents=True, exist_ok=True)
    (dest / STAGE_MARKER).write_text("Written by tools/build_wiki.py. The next build replaces everything in this folder.\n", encoding="utf-8")
    wanted = set(files) | {STAGE_MARKER}
    for existing in sorted(dest.rglob("*"), reverse=True):
        rel = existing.relative_to(dest).as_posix()
        if existing.is_file() and rel not in wanted:
            existing.unlink()
        elif existing.is_dir() and not any(existing.iterdir()):
            existing.rmdir()
    for rel, source in files.items():
        data = source.read_bytes() if isinstance(source, Path) else source
        target = dest / rel
        if target.is_file() and target.read_bytes() == data:
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(data)


def write_if_changed(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    data = text.encode("utf-8")
    if not (path.is_file() and path.read_bytes() == data):
        path.write_bytes(data)


def check_site_dir(site_dir):
    ensure_not_dangerous(site_dir, "site")
    if site_dir.exists() and any(site_dir.iterdir()):
        looks_like_site = (site_dir / "index.html").is_file() and (site_dir / "assets").is_dir() and (site_dir / "search").is_dir()
        if not looks_like_site:
            raise BuildError("%s is not empty and does not look like an earlier build of the wiki; MkDocs would delete its "
                             "contents. Choose an empty folder with --out." % site_dir)


# ------------------------------------------------------------------------------------------------------------------------
# Running
# ------------------------------------------------------------------------------------------------------------------------

def stage_everything(args, quiet=False):
    stage, site, nav = build_stage(args.allow_missing_images, args.allow_missing_files)
    for warning in stage.warnings:
        print("wiki: warning: " + warning, file=sys.stderr)
    if stage.problems:
        for problem in stage.problems:
            print("wiki: error: " + problem, file=sys.stderr)
        raise BuildError("%d problem%s in the sources; nothing was built" % (len(stage.problems), "" if len(stage.problems) == 1 else "s"))
    sync_tree(args.stage, stage.files)
    config_path = args.stage.with_name(args.stage.name + ".mkdocs.yml")
    write_if_changed(config_path, mkdocs_config(site, nav, args.stage, args.out))
    if not quiet:
        print("wiki: staged %d pages and %d other files in %s" % (len(stage.pages), len(stage.files) - len(stage.pages), args.stage))
    return stage, config_path


def mkdocs_command(*words):
    return [sys.executable, "-m", "mkdocs"] + list(words)


def mkdocs_env():
    env = dict(os.environ)
    env["NO_MKDOCS_2_WARNING"] = "1"      # Material's notice about MkDocs 2.0; the versions are pinned in requirements.txt
    env["PYTHONUTF8"] = "1"
    return env


def require_mkdocs():
    try:
        import material  # noqa: F401
        import mkdocs  # noqa: F401
    except ImportError:
        raise BuildError("MkDocs is not installed. Run: pip install -r tools/wiki/requirements.txt")


def source_fingerprint():
    """What --serve watches: the pages, pictures, repository documents, catalogue, settings and templates."""
    files = [ROOT / rel for rel in REPO_PAGES.values()] + [ROOT / "CHANGELOG.md", CATALOGUE, ROOT / "Directory.Build.props"]
    for base in (SRC, IMAGES, TOOLS):
        files.extend(path for path in base.rglob("*") if path.is_file())
    stamp = []
    for path in files:
        try:
            info = path.stat()
            stamp.append((str(path), info.st_mtime_ns, info.st_size))
        except OSError:
            pass
    return sorted(stamp)


def serve(args):
    process = subprocess.Popen(mkdocs_command("serve", "-f", str(args.config), "--dev-addr", args.dev_addr, "--no-strict"),
                               env=mkdocs_env())
    last = source_fingerprint()
    try:
        while process.poll() is None:
            time.sleep(1.0)
            current = source_fingerprint()
            if current != last:
                last = current
                try:
                    stage_everything(args, quiet=True)
                    print("wiki: sources changed, staged again")
                except BuildError as error:
                    print("wiki: error: %s (the page on screen is the last good one)" % error, file=sys.stderr)
    except KeyboardInterrupt:
        process.terminate()
    return process.wait()


def main():
    parser = argparse.ArgumentParser(description="Build the Dyncamelo wiki with MkDocs (Material theme).")
    parser.add_argument("--out", type=Path, default=SITE_DEFAULT, help="folder for the finished site (default build/wiki-site)")
    parser.add_argument("--stage", type=Path, default=STAGE_DEFAULT, help="staging folder for the pages (default build/wiki-docs)")
    parser.add_argument("--serve", action="store_true", help="serve the wiki with live reload instead of building the site")
    parser.add_argument("--dev-addr", default="127.0.0.1:8000", help="address for --serve (default 127.0.0.1:8000)")
    parser.add_argument("--allow-missing-images", action="store_true", help="a missing picture is a warning, not an error (for drafts)")
    parser.add_argument("--allow-missing-files", action="store_true", help="a link to a file that is not there yet (a graph to download) is a warning, not an error")
    parser.add_argument("--draft", action="store_true", help="both of the above")
    parser.add_argument("--stage-only", action="store_true", help="only write the staging folder and the MkDocs file")
    args = parser.parse_args()
    if args.draft:
        args.allow_missing_images = args.allow_missing_files = True
    args.out = args.out.resolve()
    args.stage = args.stage.resolve()

    try:
        if not args.stage_only:
            require_mkdocs()
            check_site_dir(args.out)
        stage, args.config = stage_everything(args)
        if args.stage_only:
            return 0
        if args.serve:
            return serve(args)
        result = subprocess.run(mkdocs_command("build", "--strict", "-f", str(args.config)), env=mkdocs_env())
        if result.returncode != 0:
            print("wiki: the MkDocs build failed (a warning counts as a failure)", file=sys.stderr)
            return result.returncode
        print("wiki: built %d pages in %s" % (len(stage.pages), args.out))
        return 0
    except BuildError as error:
        print("wiki: error: %s" % error, file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
