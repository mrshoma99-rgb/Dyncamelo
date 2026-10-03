"""MkDocs hooks for the Dyncamelo wiki.

The build (tools/build_wiki.py) puts `edit_path: docs/wiki-src/installation.md` (the file in the repository) in the front
matter of every page that is written by hand or is a repository document. This hook turns it into the "edit this page"
link of the theme. Pages the build generates (node reference, what's new) have no `edit_path`, so they get no edit link.
"""


def on_page_context(context, page, config, nav):
    meta = page.meta or {}
    path = meta.get("edit_path")
    repo = (config.extra or {}).get("repo_url")
    if path and repo:
        page.edit_url = "%s/edit/main/%s" % (repo.rstrip("/"), path)
    else:
        page.edit_url = None
    return context
