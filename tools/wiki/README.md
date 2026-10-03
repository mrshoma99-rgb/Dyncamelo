# The Dyncamelo wiki: how it is built

The wiki (the user guide for bimcamel.com) is built with [MkDocs](https://www.mkdocs.org/) and the [Material for MkDocs](https://squidfunk.github.io/mkdocs-material/) theme. Both are open source (MIT and BSD-2). The result is plain static HTML, CSS and JavaScript. It needs no server software, and it also works when you open `index.html` from a folder.

Nothing the build makes is committed. `build/` is ignored by git. CI builds the wiki on every push and keeps the site as the `dyncamelo-wiki` artifact.

## Build it

You need Python 3.11 or newer (checked with 3.11 and 3.12; the pinned versions do not install on 3.10).

```
python -m venv build/venv
build\venv\Scripts\activate          # on Linux and macOS: source build/venv/bin/activate
pip install -r tools/wiki/requirements.txt
python tools/build_wiki.py
```

The site is written to `build/wiki-site`. Open `build/wiki-site/index.html` to look at it.

The build is **strict**: any warning fails it. That includes a link to a page that does not exist, a link to a heading that does not exist, and a picture that is missing.

| Command | What it does |
|---|---|
| `python tools/build_wiki.py` | Builds the site into `build/wiki-site`. |
| `python tools/build_wiki.py --serve` | Builds, then serves the wiki on http://127.0.0.1:8000 and rebuilds when you save a page, a picture, a repository document or a setting. Press Ctrl+C to stop. `--dev-addr HOST:PORT` changes the address. |
| `python tools/build_wiki.py --draft` | A picture or a graph file that is not in the repository yet is a warning, not an error. The page shows "picture to come" or "(file to come)" in its place. CI uses this until all pictures are committed. |
| `--allow-missing-images`, `--allow-missing-files` | The two halves of `--draft`. |
| `python tools/build_wiki.py --stage-only` | Only writes the staging folder (`build/wiki-docs`) and the MkDocs file for it. |
| `--out DIR`, `--stage DIR` | Choose the folders for the site and for the staging pages. The build refuses a folder that is not empty and does not look like an earlier build. |

## Where things come from

The build copies everything into a staging folder, `build/wiki-docs`, and then runs `mkdocs build --strict` on it.

| Page | Source |
|---|---|
| Hand-written pages | `docs/wiki-src/*.md` |
| How-to guides | `docs/wiki-src/howto/*.md` |
| Troubleshooting, Recipes, Writing your own nodes | `docs/TROUBLESHOOTING.md`, `docs/RECIPES.md`, `docs/EXTENDING.md`. The links in them are turned into links of the site, or into links to the file on GitHub. Their headings get the same ids as on GitHub, so links written for GitHub still work. Do not make `docs/wiki-src/troubleshooting.md` and so on; the build refuses it. |
| What's new | `CHANGELOG.md` |
| Node reference | `docs/dyncamelo-nodes.json` (made by `tools/generate_node_catalog.py`): one page per category group, and every node has the anchor `node-<name>`. |
| Pictures | `docs/images/` |
| Graph files | `docs/wiki-src/graphs/*.dyc` |
| Menu and tabs | `docs/wiki-src/nav.yml` |
| Colours, plug-ins, Markdown features | `tools/wiki/mkdocs.yml` |
| Addresses (GitHub, App Store, bimcamel.com) | `tools/wiki/site.yml` |
| Header, footer, announcement bar, styles | `tools/wiki/overrides/` |

A page file that nav.yml does not mention and no `glob` section matches is an error. So is a nav.yml key that has no file.

## Add a page

1. Write `docs/wiki-src/<name>.md`. The first line must be `# Title`.
2. Add `{key: <name>, label: <menu text>}` to a section of `docs/wiki-src/nav.yml`.
3. Run `python tools/build_wiki.py --serve` and look at it.

Links between pages are ordinary Markdown links to the file: `[Concepts](concepts.md#lists-replication-and-lacing)`. The anchor is the heading text in lower case with hyphens, as on GitHub.

## Add a how-to guide

Put `docs/wiki-src/howto/<name>.md` there. Nothing else to change: the "How-to guides" section of nav.yml takes every file in that folder. The page may start with a block of front matter:

```
---
title: Colour elements by a property    # optional; the menu text. Default: the first "# " heading
summary: One sentence for search engines and link previews.
order: 20                                 # optional; the menu is sorted by this number, then by file name
---
# Colour elements by a property
```

## Add a picture

Pictures are made by the tests in `tests/Dyncamelo.UI.Tests/Wiki` (the agreed list is in `tools/wiki/image-manifest.md`) and committed to `docs/images/`.

* `docs/images/<id>.png` is the picture for the **dark** theme. `docs/images/<id>-light.png` is the same picture for the **light** theme. If only the first exists, it is shown in both themes.
* In a page, write the picture **once**, with its caption as the text in the brackets: `![The editor after a run](../images/wiki-editor-overview.png)`. From a page in `docs/wiki-src/`, write `images/<id>.png`; from a how-to, `../images/<id>.png`. The build looks the file up by name in `docs/images/`, so the folder part does not matter.
* The build wraps a picture that is alone on its line in a figure with the caption, shows each theme its own version, and lets the reader click the picture to see it large.
* A picture that does not exist is an error (a warning with `--draft`).

To offer a graph for download, put the file in `docs/wiki-src/graphs/` and link it: `[Download the graph](../graphs/<name>.dyc)`.

## What you can use in a page

Material's Markdown features are on: notes (`!!! note "Title"`), folding blocks (`??? question "Title"`), tabs (`=== "Tab"`), code blocks with a copy button, key caps (`++ctrl+shift+p++`), definition lists, icons (`:material-magnify:`) and cards:

```
<div class="grid cards" markdown>

- :material-download: **[Install it](installation.md)**

    Needs Navisworks 2024, 2025 or 2026.

- :material-rocket-launch: **[Your first script](first-steps.md)**

</div>
```

Add `quick-links` to the class (`grid cards quick-links`) for a row of small cards.

## The addresses and the Autodesk App Store button

`tools/wiki/site.yml` holds the four addresses the header buttons, the announcement bar, the footer and the "edit this page" links use: `github_url`, `app_store_url`, `bimcamel_url` and `repo_url`.

**`app_store_url` is empty on purpose.** While it is empty, the second header button is greyed out and says "Autodesk App Store - coming soon" (it is not a link), and the announcement bar says the Autodesk App Store copy for professional use is coming soon. When the listing exists, put its address between the quotes. The button then becomes a normal button, and the bar links to it. Nothing else changes.

The "edit this page" button links to the page's file on GitHub. Pages that the build generates (the node reference and What's new) have none.

## Publish it

1. Build the site: `python tools/build_wiki.py` (without `--draft`, so a missing picture stops you).
2. Upload the **contents** of `build/wiki-site` to the web host, for example into the folder that serves `https://www.bimcamel.com/plugins/dyncamelo/`. The site has no `site_url` and every link is relative, so it works in any folder.
3. Or zip the folder. The unzipped copy works from the file system too (the `offline` plug-in loads the search index as a script), and so does the search.

The page asks no other server for anything: no web fonts, no analytics, no scripts from a network. Links such as GitHub open only when the reader clicks them.

## Versions are pinned

`tools/wiki/requirements.txt` lists exact versions of everything. Material for MkDocs is in maintenance mode, and MkDocs 2.0 will break plug-ins and theme overrides, which this wiki uses. Do not loosen the pins. To update, install the new versions in a clean environment, build, look at the result (search, light and dark, a phone width), and then change the file.

The built site contains MIT-licensed parts of the theme. They are listed in `THIRD-PARTY-NOTICES.md`.

## Search

The search finds pages, headings and nodes. A node name is one word, so "overridecolor", "override" and "Appearance.OverrideColor" all find `Appearance.OverrideColor`. A node's own entry counts a little more than a page that only mentions it (`NODE_BOOST` in `tools/wiki/hooks.py`). To keep a section out of the search, add `{ data-search-exclude }` to its heading.
