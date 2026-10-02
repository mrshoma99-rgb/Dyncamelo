# Dyncamelo v0.46.0: source code release

This release is the **source code only**. It has no installer and no ready-to-install bundle. The "Source code" archives under Assets are GitHub's snapshot of the repository at this version. To run Dyncamelo, build it from source (README, "Build from source") with Navisworks 2024, 2025 or 2026 on Windows.

## New licence

From this version Dyncamelo is licensed under the **PolyForm Noncommercial License 1.0.0** (see `LICENSE`). It is free for personal use and other noncommercial use: hobby projects, learning, research, charities, schools, public bodies. Using it for work at a company, or in a paid project or product, needs a commercial licence from BIMCamel: open an issue titled "Commercial licence" or get in touch through bimcamel.com.

Releases up to 0.45.1 were published under Apache 2.0 with the Commons Clause. Copies you already have stay under that licence; its text is kept in `docs/licenses`.

## What is new since 0.45.1

* **Help > Run Self-Test** runs 29 read-only checks on the Navisworks nodes against the open model and reports pass, fail or skip for each. The report goes to the clipboard.
* **Help > Copy Diagnostics** copies versions, the installed plug-in bundles, the libraries loaded and the end of the error log, with user and computer names replaced, ready to paste into an issue.
* **A question before a graph from a file runs** when it contains nodes that start programs, use the network or delete, move or overwrite files. A graph saved with automatic running is no longer run when it is opened from a file. New setting: *Ask before running graphs from files*.
* **Help > Privacy Policy** shows the privacy policy inside the app. **Settings > Privacy** switches the daily update check off.
* The Script Player also asks about scripts whose only risky nodes run programs or use the network.
* The node library no longer lists a stray "WatchTableNode" category.
* The ribbon tab is no longer merged with other BIMCamel tools' tabs (that code reached into Autodesk's ribbon assembly, which is not a public API).
* New documents: security policy, changelog, troubleshooting, QA checklist, issue forms, privacy policy. A pictured README.

The full list is in `CHANGELOG.md`.
