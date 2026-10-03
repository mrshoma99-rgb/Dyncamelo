#!/usr/bin/env python3
"""Builds the Dyncamelo wiki: a static, offline-capable site in docs/wiki/ for publishing on bimcamel.com.

Sources
  docs/wiki-src/*.md           hand-written pages (one Markdown file per page; first line '# Title')
  docs/dyncamelo-nodes.json    the node catalogue (written by tools/generate_node_catalog.py): one reference page per
                               category with every node's description, inputs and outputs
  CHANGELOG.md                 becomes the 'What's new' page
  docs/images/*                pictures a page refers to are copied next to the site

Usage
  python tools/build_wiki.py            write docs/wiki
  python tools/build_wiki.py --check    fail (exit 1) when docs/wiki is not what a build would write (used by CI)
  python tools/build_wiki.py --out DIR  write somewhere else (for the CI artifact)

Needs the 'markdown' package (pip install markdown). The output has no timestamps, so a build is repeatable.
"""

import argparse
import html
import json
import os
import re
import shutil
import sys
from pathlib import Path

try:
    import markdown
except ImportError:  # pragma: no cover - the message is the point
    sys.exit("build_wiki.py needs the 'markdown' package: pip install markdown")

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "docs" / "wiki-src"
OUT_DEFAULT = ROOT / "docs" / "wiki"
CATALOGUE = ROOT / "docs" / "dyncamelo-nodes.json"
IMAGES = ROOT / "docs" / "images"
SITE_URL = "https://www.bimcamel.com/plugins/dyncamelo"
REPO_URL = "https://github.com/mrshoma99-rgb/dyncamelo"

# The order and grouping of the sidebar. A page that is not listed here is a build error, and so is a listed page without a file.
NAV = [
    ("Start here", [
        ("index", "Home"),
        ("installation", "Installation"),
        ("requirements", "Requirements"),
        ("first-steps", "Your first script"),
        ("updating", "Updating"),
        ("uninstall", "Uninstalling"),
    ]),
    ("Using Dyncamelo", [
        ("concepts", "Concepts"),
        ("ports-and-kinds", "Inputs, outputs and kinds"),
        ("canvas-and-nodes", "The editor"),
        ("library-and-search", "Node library and search"),
        ("running-graphs", "Running a graph"),
        ("saving-opening", "Saving and opening"),
        ("node-groups", "Node groups"),
        ("player", "The Script Player"),
        ("settings", "Settings"),
        ("shortcuts", "Keyboard and mouse"),
    ]),
    ("Learn by example", [
        ("samples", "Sample scripts"),
        ("recipes", "Recipes"),
        ("exchange-formats", "IFC, BCF, Excel and CSV"),
    ]),
    ("Help", [
        ("troubleshooting", "Troubleshooting"),
        ("faq", "FAQ"),
        ("privacy-and-safety", "Privacy and safety"),
        ("licence", "Licence"),
        ("whats-new", "What's new"),
    ]),
    ("For developers", [
        ("extending", "Writing your own nodes"),
    ]),
]
OPTIONAL_PAGES = set()  # pages that may be missing without failing a non-strict build

# Pages whose text lives in the repository's own documents, so there is one copy of it: the wiki page is that document with its
# links turned into links of the site (or, for files the site does not have, into the file on GitHub).
REPO_PAGES = {
    "troubleshooting": "docs/TROUBLESHOOTING.md",
    "recipes": "docs/RECIPES.md",
    "extending": "docs/EXTENDING.md",
}

# Where a link from a repository document goes on the site (the anchor is kept when the target page has it).
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

NODE_SECTION_TITLE = "Node reference"


def read_version():
    props = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    match = re.search(r"<Version>\s*([0-9]+\.[0-9]+\.[0-9]+)\s*</Version>", props)
    return match.group(1) if match else "0.0.0"


def slug(text):
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


def esc(text):
    return html.escape(str(text), quote=True)


# ------------------------------------------------------------------------------------------------------------------------
# Pages
# ------------------------------------------------------------------------------------------------------------------------

class Page:
    def __init__(self, key, title, body_html, toc, kind="page", summary=""):
        self.key = key          # "installation" or "nodes/search"
        self.title = title
        self.body = body_html
        self.toc = toc          # [(level, id, text)]
        self.kind = kind
        self.summary = summary

    @property
    def path(self):
        return self.key + ".html"

    @property
    def depth(self):
        return self.key.count("/")


def github_slug(value, separator="-"):
    """Heading ids the way GitHub makes them (punctuation dropped, each space a hyphen), so the links inside the repository's own
    documents, which were written for GitHub, find their headings on the site too."""
    value = re.sub(r"[^\w\- ]", "", value.strip().lower(), flags=re.UNICODE)
    return value.replace(" ", separator)


def markdown_to_html(text):
    md = markdown.Markdown(extensions=["tables", "fenced_code", "toc", "sane_lists", "attr_list"],
                           extension_configs={"toc": {"permalink": False, "slugify": github_slug}})
    body = md.convert(text)
    body = re.sub(r"^\s*<h1[^>]*>.*?</h1>\s*", "", body, count=1, flags=re.S)   # the page template draws the title itself
    toc = []

    def walk(tokens):
        for token in tokens:
            if token["level"] in (2, 3):
                toc.append((token["level"], token["id"], token["name"]))
            walk(token.get("children", []))

    walk(md.toc_tokens)
    return body, toc


def first_paragraph_text(markdown_text):
    lines = markdown_text.splitlines()
    seen_title = False
    for line in lines:
        stripped = line.strip()
        if not seen_title:
            if stripped.startswith("# "):
                seen_title = True
            continue
        if stripped and not stripped.startswith("#") and not stripped.startswith(("|", "```", "-", "*", ">")):
            return re.sub(r"[`*_\[\]]", "", re.sub(r"\]\([^)]*\)", "", stripped))
    return ""


def repo_document(key):
    """The text of a page that is a repository document, with its links turned into links of the site."""
    rel = REPO_PAGES[key]
    text = (ROOT / rel).read_text(encoding="utf-8").replace("\r\n", "\n")
    base = Path(rel).parent

    def fix(match):
        target = match.group(1)
        if re.match(r"^(https?:|mailto:|#)", target):
            return match.group(0)
        path, _, anchor = target.partition("#")
        resolved = os.path.normpath((base / path).as_posix()).replace("\\", "/")
        for name, page in LINK_MAP.items():
            if resolved in ("docs/" + name, name):
                suffix = ("#" + anchor) if anchor and page == key + ".md" else ""
                return "](%s%s)" % (page, suffix)
        return "](%s/blob/main/%s%s)" % (REPO_URL, resolved, ("#" + anchor) if anchor else "")

    return re.sub(r"\]\(([^)\s]+)\)", fix, text)


def load_handwritten(strict, problems):
    pages = {}
    for _, items in NAV:
        for key, label in items:
            if key == "whats-new":
                continue
            if key in REPO_PAGES:
                if (SRC / (key + ".md")).exists():
                    problems.append("docs/wiki-src/%s.md must not exist: the page is built from %s" % (key, REPO_PAGES[key]))
                    continue
                text = repo_document(key)
                if not text.lstrip().startswith("# "):
                    problems.append("%s must start with '# Title'" % REPO_PAGES[key])
                    continue
                title = text.lstrip().split("\n", 1)[0][2:].strip()
                body, toc = markdown_to_html(text)
                pages[key] = Page(key, title, body, toc, summary=first_paragraph_text(text))
                continue
            path = SRC / (key + ".md")
            if not path.exists():
                if strict and key not in OPTIONAL_PAGES:
                    problems.append("missing page: docs/wiki-src/%s.md (listed in NAV as '%s')" % (key, label))
                continue
            text = path.read_text(encoding="utf-8").replace("\r\n", "\n")
            if not text.lstrip().startswith("# "):
                problems.append("docs/wiki-src/%s.md must start with '# Title'" % key)
                continue
            title = text.lstrip().split("\n", 1)[0][2:].strip()
            body, toc = markdown_to_html(text)
            pages[key] = Page(key, title, body, toc, summary=first_paragraph_text(text))
    listed = {key for _, items in NAV for key, _ in items}
    if SRC.exists():
        for path in sorted(SRC.rglob("*.md")):
            key = path.relative_to(SRC).with_suffix("").as_posix()
            if key not in listed and key != "README" and not key.startswith(("howto/", "graphs/")) and key != "glossary":   # these are built by the MkDocs pipeline that replaces this script
                problems.append("docs/wiki-src/%s.md is not listed in NAV (tools/build_wiki.py)" % key)
    return pages


def changelog_page():
    text = (ROOT / "CHANGELOG.md").read_text(encoding="utf-8").replace("\r\n", "\n")
    lines = text.split("\n")
    # drop the file's own '# Changelog' line and its two intro paragraphs about how it was made; keep from the first '## ' on
    start = next((i for i, line in enumerate(lines) if line.startswith("## ")), 0)
    intro = ("# What's new\n\nEvery release of Dyncamelo and what changed in it, newest first. "
             "The newest section, *Unreleased*, lists what is already in the source and not yet in a numbered release.\n\n")
    text = "\n".join(lines[start:])
    # the changelog links to files of the repository; on the site those are the repository's pages
    text = re.sub(r"\]\((?!https?:|#|mailto:)([^)\s]+)\)", lambda m: "](%s/blob/main/%s)" % (REPO_URL, m.group(1).lstrip("./")), text)
    body, toc = markdown_to_html(intro + text)
    return Page("whats-new", "What's new", body, toc, summary="Every release of Dyncamelo and what changed in it.")


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


def format_default(value):
    if value is None:
        return ""
    return str(value)


def render_node(node):
    out = ['<section class="node" id="%s">' % node_anchor(node["name"])]
    out.append('<h3><a class="node-name" href="#%s">%s</a></h3>' % (node_anchor(node["name"]), esc(node["name"])))
    if node.get("interactive"):
        out.append('<p class="node-flag">Interactive node: it has its own controls on the canvas.</p>')
    out.append("<p>%s</p>" % esc(node["description"]))
    if node.get("inputs"):
        out.append('<table class="ports"><thead><tr><th>Input</th><th>Type</th><th>Default</th><th>What it does</th></tr></thead><tbody>')
        for port in node["inputs"]:
            flags = ' <span class="chip">any number of wires</span>' if port.get("multiInput") else ""
            optional = "" if "default" in port else ' <span class="chip required">required</span>'
            out.append("<tr><td><code>%s</code>%s%s</td><td><code>%s</code></td><td>%s</td><td>%s</td></tr>" % (
                esc(port["name"]), flags, optional, esc(port["type"]),
                ("<code>%s</code>" % esc(format_default(port["default"]))) if "default" in port else "&mdash;",
                esc(port.get("description", ""))))
        out.append("</tbody></table>")
    else:
        out.append('<p class="muted">No inputs.</p>')
    if node.get("outputs"):
        described = any(port.get("description") for port in node["outputs"])
        out.append('<table class="ports"><thead><tr><th>Output</th><th>Type</th>%s</tr></thead><tbody>' % ("<th>What it gives</th>" if described else ""))
        for port in node["outputs"]:
            out.append("<tr><td><code>%s</code></td><td><code>%s</code></td>%s</tr>" % (
                esc(port["name"]), esc(port["type"]), ("<td>%s</td>" % esc(port.get("description", ""))) if described else ""))
        out.append("</tbody></table>")
    else:
        out.append('<p class="muted">No outputs.</p>')
    if node.get("returns"):
        out.append('<p class="muted">Returns: %s</p>' % esc(node["returns"]))
    tags = node.get("tags") or []
    if tags:
        out.append('<p class="tags">%s</p>' % " ".join('<span class="chip">%s</span>' % esc(tag) for tag in tags))
    out.append("</section>")
    return "\n".join(out)


def build_node_pages(catalogue):
    nodes = catalogue["nodes"]
    groups = {}
    for node in nodes:
        groups.setdefault(node_group(node["category"]), []).append(node)
    pages = []
    group_index = []
    for group in sorted(groups, key=lambda g: (g.startswith("Navisworks"), g.lower())):
        items = sorted(groups[group], key=lambda n: (n["category"].lower(), n["name"].lower()))
        key = "nodes/" + slug(group)
        toc = []
        body = ['<p class="lead">%d node%s. Every node has its own link: click its name. Types ending in <code>[]</code> are lists; an input with a default is optional.</p>' % (
            len(items), "" if len(items) == 1 else "s")]
        current = None
        for node in items:
            if node["category"] != current:
                current = node["category"]
                if current != group:
                    heading = current[len(group) + 1:] if current.startswith(group + ".") else current
                    body.append('<h2 id="%s">%s</h2>' % (slug(current), esc(heading)))
                    toc.append((2, slug(current), heading))
            body.append(render_node(node))
        page = Page(key, group_title(group), "\n".join(body), toc, kind="nodes", summary="%d nodes: inputs, outputs and what each does." % len(items))
        pages.append(page)
        group_index.append((group, key, len(items)))
    return pages, group_index, groups


def build_nodes_index(group_index, groups):
    body = ['<p class="lead">Every node in the library, with its inputs and outputs. Pick a category, or type in the box to filter all %d nodes.</p>' % sum(c for _, _, c in group_index)]
    body.append('<h2 id="categories">Categories</h2><div class="cards">')
    for group, key, count in group_index:
        names = groups[group]
        sample = ", ".join(n["name"].split(".")[-1] for n in sorted(names, key=lambda n: n["name"].lower())[:4])
        body.append('<a class="card" href="%s.html"><strong>%s</strong><span>%d node%s</span><em>%s&hellip;</em></a>' % (
            key.split("/", 1)[1], esc(group_title(group)), count, "" if count == 1 else "s", esc(sample)))
    body.append("</div>")
    body.append('<h2 id="all-nodes">All nodes</h2>')
    body.append('<input id="node-filter" class="node-filter" type="search" placeholder="Filter by name, category or word in the description" aria-label="Filter nodes">')
    body.append('<table class="ports all-nodes"><thead><tr><th>Node</th><th>Category</th><th>What it does</th></tr></thead><tbody>')
    rows = []
    for group, key, _ in group_index:
        for node in sorted(groups[group], key=lambda n: n["name"].lower()):
            summary = node["description"]
            cut = summary.find(". ")
            short = summary if cut < 0 else summary[:cut + 1]
            rows.append('<tr data-search="%s"><td><a href="%s.html#%s"><code>%s</code></a></td><td>%s</td><td>%s</td></tr>' % (
                esc((node["name"] + " " + node["category"] + " " + node["description"] + " " + " ".join(node.get("tags") or [])).lower()),
                key.split("/", 1)[1], node_anchor(node["name"]), esc(node["name"]), esc(node["category"]), esc(short)))
    body.extend(rows)
    body.append("</tbody></table>")
    toc = [(2, "categories", "Categories"), (2, "all-nodes", "All nodes")]
    return Page("nodes/index", "Node reference", "\n".join(body), toc, kind="nodes-index",
                summary="Every node with its inputs and outputs, by category.")


# ------------------------------------------------------------------------------------------------------------------------
# Rendering
# ------------------------------------------------------------------------------------------------------------------------

CSS = """
:root{--bg:#f7f8fa;--panel:#ffffff;--text:#1d2430;--muted:#5d6877;--line:#d9dee6;--accent:#0a74da;--accent-soft:#e3f0fc;--code:#eef1f5;--warn:#b86e00}
:root[data-theme=dark]{--bg:#15171b;--panel:#1c1f24;--text:#e6e9ee;--muted:#9aa4b2;--line:#333941;--accent:#4aa8f5;--accent-soft:#1f2d3d;--code:#252a31;--warn:#f0c66a}
@media (prefers-color-scheme:dark){:root:not([data-theme=light]){--bg:#15171b;--panel:#1c1f24;--text:#e6e9ee;--muted:#9aa4b2;--line:#333941;--accent:#4aa8f5;--accent-soft:#1f2d3d;--code:#252a31;--warn:#f0c66a}}
*{box-sizing:border-box}html{scroll-behavior:smooth}
body{margin:0;background:var(--bg);color:var(--text);font:16px/1.6 "Segoe UI",system-ui,-apple-system,Roboto,sans-serif}
a{color:var(--accent);text-decoration:none}a:hover{text-decoration:underline}
header.top{position:sticky;top:0;z-index:20;display:flex;align-items:center;gap:16px;padding:10px 20px;background:var(--panel);border-bottom:1px solid var(--line)}
header.top .brand{font-weight:700;font-size:18px;color:var(--text);white-space:nowrap}
header.top .brand small{font-weight:400;color:var(--muted);margin-left:8px;font-size:13px}
header.top .spacer{flex:1}
#search{width:min(360px,40vw);padding:7px 12px;border:1px solid var(--line);border-radius:8px;background:var(--bg);color:var(--text);font:inherit;font-size:14px}
#results{position:absolute;top:52px;right:130px;width:min(520px,90vw);max-height:70vh;overflow:auto;background:var(--panel);border:1px solid var(--line);border-radius:10px;box-shadow:0 8px 28px rgba(0,0,0,.25);display:none}
#results a{display:block;padding:8px 14px;color:var(--text);border-bottom:1px solid var(--line)}
#results a:hover{background:var(--accent-soft);text-decoration:none}
#results small{display:block;color:var(--muted)}
button.theme{border:1px solid var(--line);background:var(--bg);color:var(--text);border-radius:8px;padding:6px 10px;cursor:pointer;font:inherit;font-size:13px}
.layout{display:grid;grid-template-columns:260px minmax(0,1fr) 220px;gap:28px;max-width:1500px;margin:0 auto;padding:0 20px}
nav.side{position:sticky;top:60px;align-self:start;max-height:calc(100vh - 70px);overflow:auto;padding:18px 0;font-size:14.5px}
nav.side h4{margin:16px 0 4px;font-size:11.5px;letter-spacing:.06em;text-transform:uppercase;color:var(--muted)}
nav.side a{display:block;padding:4px 10px;border-radius:6px;color:var(--text)}
nav.side a:hover{background:var(--accent-soft);text-decoration:none}
nav.side a.current{background:var(--accent-soft);color:var(--accent);font-weight:600}
nav.side details.nodes-nav{margin-top:16px}nav.side details summary{cursor:pointer;font-size:11.5px;letter-spacing:.06em;text-transform:uppercase;color:var(--muted);padding:2px 0 4px}
nav.side a.sub{padding-left:22px;font-size:13.5px;color:var(--muted)}
nav.side a.sub.current{color:var(--accent)}
main{padding:26px 0 60px;min-width:0}
main h1{font-size:2rem;margin:.2em 0 .3em}
main h2{margin-top:2em;padding-top:.4em;border-top:1px solid var(--line)}
main h3{margin-top:1.6em}
main img{max-width:100%;border:1px solid var(--line);border-radius:8px}
main table{border-collapse:collapse;width:100%;margin:1em 0;font-size:14.5px;display:block;overflow-x:auto}
main th,main td{border:1px solid var(--line);padding:6px 10px;text-align:left;vertical-align:top}
main th{background:var(--panel)}
main code{background:var(--code);padding:1px 5px;border-radius:4px;font:13.5px/1.4 Consolas,"Cascadia Mono",monospace}
main pre{background:var(--code);padding:12px 14px;border-radius:8px;overflow:auto}
main pre code{background:none;padding:0}
main blockquote{margin:1em 0;padding:.4em 1em;border-left:4px solid var(--accent);background:var(--accent-soft);border-radius:0 8px 8px 0}
.lead{font-size:1.1rem;color:var(--muted)}.muted{color:var(--muted)}
aside.toc{position:sticky;top:60px;align-self:start;max-height:calc(100vh - 70px);overflow:auto;padding:26px 0;font-size:13.5px}
aside.toc h4{margin:0 0 6px;font-size:11.5px;letter-spacing:.06em;text-transform:uppercase;color:var(--muted)}
aside.toc a{display:block;padding:2px 0;color:var(--muted)}aside.toc a.l3{padding-left:12px}
.cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr));gap:12px;margin:1em 0}
.card{display:flex;flex-direction:column;gap:2px;padding:12px 14px;background:var(--panel);border:1px solid var(--line);border-radius:10px;color:var(--text)}
.card:hover{border-color:var(--accent);text-decoration:none}.card span{color:var(--accent);font-size:13px}.card em{color:var(--muted);font-size:12.5px;font-style:normal}
section.node{padding:6px 0 10px;border-top:1px solid var(--line)}
section.node h3{margin:.8em 0 .2em}a.node-name{color:var(--text);font-family:Consolas,"Cascadia Mono",monospace;font-size:1.05rem}
table.ports{font-size:14px}.chip{display:inline-block;padding:0 7px;border-radius:9px;background:var(--code);color:var(--muted);font-size:12px;margin:1px 2px}
.chip.required{background:transparent;border:1px solid var(--line)}.tags{margin:.4em 0}.node-flag{color:var(--warn);font-size:13.5px}
input.node-filter{width:100%;padding:9px 12px;border:1px solid var(--line);border-radius:8px;background:var(--panel);color:var(--text);font:inherit}
footer.foot{border-top:1px solid var(--line);padding:18px 20px;text-align:center;color:var(--muted);font-size:13.5px}
@media (max-width:1100px){.layout{grid-template-columns:230px minmax(0,1fr)}aside.toc{display:none}}
@media (max-width:760px){.layout{grid-template-columns:1fr}nav.side{position:static;max-height:none}#results{right:10px}}
"""

JS = """
(function(){
  var root=document.documentElement;
  try{var saved=localStorage.getItem('dyc-theme');if(saved){root.setAttribute('data-theme',saved);}}catch(e){}
  var toggle=document.getElementById('theme');
  if(toggle){toggle.addEventListener('click',function(){
    var dark=root.getAttribute('data-theme')==='dark'||(!root.getAttribute('data-theme')&&window.matchMedia&&window.matchMedia('(prefers-color-scheme: dark)').matches);
    var next=dark?'light':'dark';root.setAttribute('data-theme',next);
    try{localStorage.setItem('dyc-theme',next);}catch(e){}
  });}
  var box=document.getElementById('search'),out=document.getElementById('results'),base=document.body.getAttribute('data-base')||'';
  if(box&&window.WIKI_INDEX){
    box.addEventListener('input',function(){
      var q=box.value.toLowerCase().split(/\\s+/).filter(Boolean);
      if(!q.length){out.style.display='none';return;}
      var hits=[];
      window.WIKI_INDEX.forEach(function(e){
        var hay=(e.t+' '+e.x).toLowerCase(),score=0;
        for(var i=0;i<q.length;i++){if(hay.indexOf(q[i])<0){return;}score+=e.t.toLowerCase().indexOf(q[i])>=0?3:1;}
        hits.push({e:e,s:score});
      });
      hits.sort(function(a,b){return b.s-a.s;});
      out.innerHTML='';
      hits.slice(0,25).forEach(function(h){
        var a=document.createElement('a');a.href=base+h.e.u;
        a.innerHTML='<strong></strong><small></small>';
        a.firstChild.textContent=h.e.t;a.lastChild.textContent=h.e.k+(h.e.d?' - '+h.e.d:'');
        out.appendChild(a);
      });
      if(!hits.length){out.innerHTML='<a href="#"><small>Nothing found.</small></a>';}
      out.style.display='block';
    });
    document.addEventListener('click',function(ev){if(ev.target!==box&&!out.contains(ev.target)){out.style.display='none';}});
  }
  var filter=document.getElementById('node-filter');
  if(filter){
    var rows=document.querySelectorAll('table.all-nodes tbody tr');
    filter.addEventListener('input',function(){
      var q=filter.value.toLowerCase().split(/\\s+/).filter(Boolean);
      rows.forEach(function(r){var h=r.getAttribute('data-search')||'';var show=q.every(function(w){return h.indexOf(w)>=0;});r.style.display=show?'':'none';});
    });
  }
})();
"""


def nav_html(pages, node_groups_list, current_key, base):
    parts = []
    for section, items in NAV:
        links = []
        for key, label in items:
            if key not in pages:
                continue
            cls = ' class="current"' if key == current_key else ""
            links.append('<a%s href="%s%s.html">%s</a>' % (cls, base, key, esc(label)))
        if links:
            parts.append("<h4>%s</h4>" % esc(section))
            parts.extend(links)
        if section == "Learn by example":
            opened = " open" if current_key.startswith("nodes/") else ""
            parts.append('<details class="nodes-nav"%s><summary>%s</summary>' % (opened, NODE_SECTION_TITLE))
            cls = ' class="current"' if current_key == "nodes/index" else ""
            parts.append('<a%s href="%snodes/index.html">All nodes</a>' % (cls, base))
            for group, key, count in node_groups_list:
                cls = "sub current" if key == current_key else "sub"
                parts.append('<a class="%s" href="%s%s.html">%s <span class="muted">(%d)</span></a>' % (cls, base, key, esc(group_title(group)), count))
            parts.append("</details>")
    return "\n".join(parts)


def render_page(page, pages, node_groups_list, version, base):
    toc_links = "".join('<a class="l%d" href="#%s">%s</a>' % (lvl, tid, esc(text)) for lvl, tid, text in page.toc)
    toc_block = ('<aside class="toc"><h4>On this page</h4>%s</aside>' % toc_links) if toc_links else '<aside class="toc"></aside>'
    description = esc(page.summary or "Dyncamelo documentation")
    return """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>{title} - Dyncamelo Wiki</title>
<meta name="description" content="{description}">
<link rel="stylesheet" href="{base}assets/wiki.css">
</head>
<body data-base="{base}">
<header class="top">
<a class="brand" href="{base}index.html">Dyncamelo<small>Wiki &middot; v{version}</small></a>
<span class="spacer"></span>
<input id="search" type="search" placeholder="Search the wiki and the nodes" aria-label="Search">
<div id="results"></div>
<a href="{site}">bimcamel.com</a>
<button class="theme" id="theme" title="Switch between light and dark">Light / dark</button>
</header>
<div class="layout">
<nav class="side">{nav}</nav>
<main>
<h1>{title}</h1>
{body}
</main>
{toc}
</div>
<footer class="foot">Dyncamelo v{version} by <a href="https://www.bimcamel.com">BIMCamel</a> &middot; licensed under the PolyForm Noncommercial License 1.0.0 &middot; <a href="{repo}/issues">Report a problem</a></footer>
<script src="{base}assets/search-index.js"></script>
<script src="{base}assets/wiki.js"></script>
</body>
</html>
""".format(title=esc(page.title), description=description, base=base, version=version, site=SITE_URL, repo=REPO_URL,
           nav=nav_html(pages, node_groups_list, page.key, base), body=page.body, toc=toc_block)


# ------------------------------------------------------------------------------------------------------------------------
# Links and images
# ------------------------------------------------------------------------------------------------------------------------

def rewrite_links(page, known_keys, problems, images_used):
    base_dir = Path(page.key).parent

    def fix_href(match):
        href = match.group(1)
        if re.match(r"^(https?:|mailto:|#|/)", href):
            return match.group(0)
        target, _, anchor = href.partition("#")
        if target.endswith(".md"):
            resolved = os.path.normpath((base_dir / target).as_posix()).replace("\\", "/")
            key = resolved[:-3]
            if key not in known_keys:
                problems.append("%s links to '%s', which is not a wiki page" % (page.key, href))
                return match.group(0)
            relative = os.path.relpath(key + ".html", base_dir.as_posix() or ".").replace("\\", "/")
            return 'href="%s%s"' % (relative, ("#" + anchor) if anchor else "")
        if target.endswith(".html") or target == "":
            return match.group(0)
        problems.append("%s links to '%s' (use a wiki page .md link or an https address)" % (page.key, href))
        return match.group(0)

    def fix_src(match):
        src = match.group(1)
        if re.match(r"^(https?:|data:)", src):
            return match.group(0)
        name = Path(src).name
        if not (IMAGES / name).exists():
            problems.append("%s uses the image '%s', which is not in docs/images" % (page.key, src))
            return match.group(0)
        images_used.add(name)
        prefix = "../" * page.depth
        return 'src="%simages/%s"' % (prefix, name)

    page.body = re.sub(r'href="([^"]+)"', fix_href, page.body)
    page.body = re.sub(r'src="([^"]+)"', fix_src, page.body)


# ------------------------------------------------------------------------------------------------------------------------
# Build
# ------------------------------------------------------------------------------------------------------------------------

def build(out_dir, strict):
    problems = []
    version = read_version()
    catalogue = json.loads(CATALOGUE.read_text(encoding="utf-8"))
    pages = load_handwritten(strict, problems)
    pages["whats-new"] = changelog_page()

    node_pages, group_index, groups = build_node_pages(catalogue)
    index_page = build_nodes_index(group_index, groups)
    all_pages = dict(pages)
    all_pages[index_page.key] = index_page
    for page in node_pages:
        all_pages[page.key] = page

    known = set(all_pages)
    images_used = set()
    for page in all_pages.values():
        rewrite_links(page, known, problems, images_used)

    if problems:
        for problem in problems:
            print("wiki: " + problem, file=sys.stderr)
        return None

    files = {}
    for page in all_pages.values():
        files[page.path] = render_page(page, pages, group_index, version, "../" * page.depth)
    files["assets/wiki.css"] = CSS.strip() + "\n"
    files["assets/wiki.js"] = JS.strip() + "\n"

    index = []
    for page in all_pages.values():
        if page.kind == "nodes":
            continue
        index.append({"t": page.title, "u": page.path, "k": "Page" if page.kind != "nodes-index" else "Nodes", "d": page.summary[:110],
                      "x": " ".join(text for _, _, text in page.toc)})
    for node in catalogue["nodes"]:
        group = node_group(node["category"])
        index.append({"t": node["name"], "u": "nodes/%s.html#%s" % (slug(group), node_anchor(node["name"])), "k": "Node",
                      "d": node["description"][:110], "x": node["category"] + " " + " ".join(node.get("tags") or [])})
    index.sort(key=lambda e: (e["k"] != "Page", e["t"].lower()))
    files["assets/search-index.js"] = "window.WIKI_INDEX=" + json.dumps(index, ensure_ascii=False, separators=(",", ":")) + ";\n"
    return files, images_used


def write_tree(out_dir, files, images_used):
    if out_dir.exists():
        shutil.rmtree(out_dir)
    for rel, text in files.items():
        target = out_dir / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(text.encode("utf-8"))
    if images_used:
        (out_dir / "images").mkdir(parents=True, exist_ok=True)
        for name in sorted(images_used):
            shutil.copyfile(IMAGES / name, out_dir / "images" / name)


def tree_matches(out_dir, files, images_used):
    expected = set(files) | {"images/" + n for n in images_used}
    actual = set()
    if out_dir.exists():
        for path in out_dir.rglob("*"):
            if path.is_file():
                actual.add(path.relative_to(out_dir).as_posix())
    problems = []
    for rel in sorted(expected - actual):
        problems.append("missing from docs/wiki: " + rel)
    for rel in sorted(actual - expected):
        problems.append("not produced by the build: " + rel)
    for rel in sorted(expected & actual):
        path = out_dir / rel
        if rel in files:
            if path.read_bytes() != files[rel].encode("utf-8"):
                problems.append("out of date: " + rel)
        elif path.read_bytes() != (IMAGES / rel.split("/", 1)[1]).read_bytes():
            problems.append("out of date: " + rel)
    return problems


def main():
    parser = argparse.ArgumentParser(description="Build the Dyncamelo wiki (docs/wiki).")
    parser.add_argument("--check", action="store_true", help="fail when docs/wiki is not current")
    parser.add_argument("--out", type=Path, default=OUT_DEFAULT, help="output directory (default docs/wiki)")
    parser.add_argument("--lenient", action="store_true", help="do not fail on listed pages that have no source file yet")
    args = parser.parse_args()

    built = build(args.out, strict=not args.lenient)
    if built is None:
        return 1
    files, images_used = built
    if args.check:
        problems = tree_matches(args.out, files, images_used)
        if problems:
            for problem in problems[:40]:
                print("wiki: " + problem, file=sys.stderr)
            print("wiki: docs/wiki is not current - run: python tools/build_wiki.py", file=sys.stderr)
            return 1
        print("wiki is current - %d files" % (len(files) + len(images_used)))
        return 0
    write_tree(args.out, files, images_used)
    print("wrote %d files to %s" % (len(files) + len(images_used), args.out))
    return 0


if __name__ == "__main__":
    sys.exit(main())
