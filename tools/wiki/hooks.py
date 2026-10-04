"""MkDocs hooks for the CamelGraph wiki.

The build (tools/build_wiki.py) puts `edit_path: docs/wiki-src/installation.md` (the file in the repository) in the front
matter of every page that is written by hand or is a repository document. This hook turns it into the "edit this page"
link of the theme. Pages the build generates (node reference, what's new) have no `edit_path`, so they get no edit link.
"""

import json
import os


def on_page_context(context, page, config, nav):
    meta = page.meta or {}
    path = meta.get("edit_path")
    repo = (config.extra or {}).get("repo_url")
    if path and repo:
        page.edit_url = "%s/edit/main/%s" % (repo.rstrip("/"), path)
    else:
        page.edit_url = None
    return context


# Search: a node's own entry (its heading, "Search.ByProperty") counts more than a page that only mentions it, so typing a
# node's name puts the node first. The theme reads `boost` from every entry of the search index.
NODE_BOOST = 1.5


def on_post_build(config, **kwargs):
    path = os.path.join(config["site_dir"], "search", "search_index.json")
    if not os.path.isfile(path):
        return
    with open(path, encoding="utf-8") as handle:
        data = json.load(handle)
    for doc in data.get("docs", []):
        if "#node-" in doc.get("location", ""):
            doc["boost"] = NODE_BOOST
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(data, handle, ensure_ascii=False, separators=(",", ":"))
