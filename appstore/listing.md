# Autodesk App Store listing: CamelGraph (previously Dyncamelo)

Everything the submission form asks for, ready to paste. Limits come from Autodesk's submission guide (long description 4000 characters, logo 80 x 80 recommended, up to 10 screenshots of at most 2000 x 2000 pixels and 20 MB, each with a text, up to 4 categories per compatible product). `AppStoreFilesTests` checks the limits that can be checked. Things only the publisher can supply are marked **YOU**.

## Basics

| Field | Value |
|---|---|
| Title | CamelGraph - visual programming for Navisworks |
| Previous name | Dyncamelo (renamed to CamelGraph; say so in the description so existing users find it) |
| Type | Desktop app, plug-in |
| Programs | Navisworks Manage, Navisworks Simulate |
| Operating system | Windows 64-bit |
| Price | **YOU**: not decided yet; set it in the listing form. The free copy for personal use is on GitHub and bimcamel.com |
| Publisher name | **YOU**: the name on your Autodesk publisher profile (the package says "BIMCamel") |
| Support email | **YOU**: an address you read; also goes into `appstore/publisher.json` |
| Support / contact page | https://github.com/mrshoma99-rgb/Dyncamelo/issues |
| Privacy policy URL | https://github.com/mrshoma99-rgb/Dyncamelo/blob/main/PRIVACY.md (the same text is inside the app: Help > Privacy Policy) |
| Website | https://github.com/mrshoma99-rgb/Dyncamelo |
| Logo | `assets/icon-80.png` (80 x 80); larger versions `icon-120.png`, `icon-256.png` |

## Short description

Build Navisworks jobs as node graphs, the way Dynamo does in Revit: search, colour, select, extract data, triage clashes and make viewpoints without writing code.

## Long description

Paste the text below (plain text, 4000 characters at most; the form's editor can turn the lines starting with a dash into bullets).

```text
CamelGraph (previously Dyncamelo) brings Dynamo-style visual programming to Navisworks. Instead of writing code or macros, you drag nodes onto a canvas, wire the output of one to the input of the next and press Run. The graph reads and changes the model you have open.

WHAT YOU CAN DO
- Search a federated model by property and turn the result into selection sets
- Colour, hide or make transparent whole groups of items, colour-coded by any property
- Take properties and quantities out of the model and write them to CSV, Excel or an HTML report
- Triage and group clashes by your own rules, and make one saved viewpoint per issue in a loop
- Exchange issues with other tools as BCF, and work with TimeLiner and grids
- Compare two states of a model, and work with tables, lists, text and dates using a general-purpose library

BUILT FOR REAL PROJECTS
- More than 570 nodes in 46 categories; hover any socket to see what it expects
- Only the part of a graph that changed runs again; a node that fails shows its error and never stops Navisworks
- The Script Player runs a saved graph from a simple form, so a colleague can use a tested script without opening the editor
- Graphs are plain .dyc files you can email, keep in version control and review
- Fourteen ready-made sample graphs are built in
- Before it runs a graph from a file that starts programs, uses the network or deletes or overwrites files, CamelGraph lists those nodes and asks
- Command palette, rebindable shortcuts, light and dark themes, undo and redo, autosave and recovery

WORKS WITH
Navisworks Manage and Simulate 2024, 2025 and 2026 on Windows (64-bit). The Clash Detective nodes need Manage.

PRIVACY
CamelGraph has no account, no analytics and no licence server, and it sends nothing about you or your models anywhere. The privacy policy is inside the app under Help > Privacy Policy.

LICENCE AND SUPPORT
This is the professional copy: it comes with a commercial licence from BIMCamel for use at work (a contractor, consultancy, design office or any company) and in paid projects and products. A free copy for personal use (learning, hobby projects, research, charities, schools, public bodies) is available from github.com/mrshoma99-rgb/Dyncamelo and bimcamel.com under the PolyForm Noncommercial 1.0.0 licence. Help is in the app (F1 and Help > Run Self-Test), in the quick-start page that comes with the download and at the same address.

CamelGraph is not affiliated with or endorsed by Autodesk. Autodesk, Navisworks, Revit and Dynamo are trademarks of Autodesk, Inc.
```

## Categories (up to 4 per compatible product)

Pick the nearest ones in the form; custom categories are allowed. Suggestions: Automation and scripting; Model coordination; Quantification and data extraction; Clash detection.

## Compatible products

Tick **only the releases you have tested yourself**: the form makes you commit to supporting every one you tick. The package contains builds for all three, but until each has been run in a real Navisworks (`docs/QA_CHECKLIST.md`, plus Help > Run Self-Test) the honest list is the ones you have seen working.

| Product | Package folder | Tested by publisher |
|---|---|---|
| Navisworks Manage / Simulate 2024 | `Contents/v21` | **YOU** |
| Navisworks Manage / Simulate 2025 | `Contents/v22` | **YOU** |
| Navisworks Manage / Simulate 2026 | `Contents/v23` | **YOU** |

## Screenshots (up to 10, each needs a text)

Size: PNG, at most 2000 x 2000 pixels and 20 MB each. The pictures made by the build machine are in `assets/screenshots/` (CamelGraph on its own, no Navisworks around it, a sample graph that has run); they are regenerated by CI (`EditorScreenshotTests`). The ones with a model behind the editor have to be taken in Navisworks.

| # | What to show | Text for the form | Picture |
|---|---|---|---|
| 1 | The editor docked in Navisworks with a model open behind it and a graph that has run | Build a job as a graph: nodes on a canvas, values shown under each node | **YOU** (Navisworks) |
| 2 | A model coloured by a graph (for example the colour-by-property sample) with the graph beside it | Colour a whole model by any property and see the result at once | **YOU** (Navisworks) |
| 3 | The quick node search open with a word typed | Press Space and type to add any of more than 570 nodes | `quick-search.png` |
| 4 | The Script Player with a form filled in and results below | Run a saved graph from a simple form, without the node editor | `player.png` |
| 5 | A Watch Table showing totals | Tables, totals and reports from model data | `editor-dark.png` |
| 6 | Help > Run Self-Test with the report | Built-in self-test checks the Navisworks nodes on your model | **YOU** (Navisworks) |
| 7 | Every shortcut on one sheet | Every shortcut and mouse gesture on one sheet (F1) | `shortcuts.png` |
| 8 | The command palette with a word typed | Search every command by name with Ctrl+Shift+P | `command-palette.png` |
| 9 | Settings | Settings for appearance, canvas, editing, shortcuts and privacy | `settings.png` |
| 10 | The light theme | A light and a dark theme | `editor-light.png` |

## Quick start (the form builds the help page from these fields)

The package already contains the finished page, `Contents/Resources/Help/index.html`; if the form asks for the same content as separate fields, use these.

**Overview.** CamelGraph lets you build a Navisworks job as a graph of nodes instead of writing code. Add nodes, wire the output of one to the input of the next, press Run. The graph reads and changes the model you have open.

**Requirements.** Autodesk Navisworks Manage or Simulate 2024, 2025 or 2026 on Windows (64-bit). No licence key, account or internet connection is needed.

**Installation.** Close Navisworks, run the installer from the Autodesk App Store, start Navisworks. CamelGraph appears on the BIMCamel ribbon tab.

**Getting started.** Open a model. On the BIMCamel tab, in the Visual Programming panel, click CamelGraph. Open File > Sample Graphs and pick one, then press Run. To add a node press Space on the empty canvas, type a word and press Enter; drag from a dot on one node to a dot on another to connect them. F1 lists every shortcut.

**Uninstallation.** Close Navisworks and remove CamelGraph in Windows Settings > Apps. Your graphs and the settings folder `%APPDATA%\Dyncamelo` are left alone.

**Support.** https://github.com/mrshoma99-rgb/Dyncamelo/issues, or the support email. Include the text from Help > Copy Diagnostics.

## What's new (for each update)

Use the entry for the version in `CHANGELOG.md`; the form asks for a version number and a change description each time you update.

## EULA and licence

Autodesk gives every store app its standard end-user licence agreement; you do not need your own, and the FAQ says not to add terms that conflict with it. The source licence (PolyForm Noncommercial 1.0.0, in `LICENSE`) is shipped in the package under `Contents/Resources`, and the description above says that this paid copy comes with a commercial licence from BIMCamel, because the FAQ asks for extra conditions to be written in the description or the help file. **YOU** decide the exact terms of that commercial grant (per user or per company, perpetual or yearly, what updates include); write them down and make sure the description says the same.

Whether a paid listing whose description grants a commercial licence on top of the source licence is accepted next to the standard EULA is for Autodesk to say; the publisher asks before submitting.
