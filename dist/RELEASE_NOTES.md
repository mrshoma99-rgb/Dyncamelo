# CamelGraph v0.51.1: a fix for the Markup nodes (personal-use edition with installer)

> **Personal use only.** This download (GitHub, bimcamel.com) is the free *Personal use* edition under the PolyForm Noncommercial License 1.0.0: learning, hobby projects, research, and charities, schools, universities, public research bodies and government institutions. **For professional use** (work at a company, a consultancy or design office, a paid project or product) a copy for the **Autodesk App Store** is **coming soon**; it will come with a commercial licence from BIMCamel. It is the same program in both editions.

## What is new since 0.51.0

* **Markup nodes work on viewpoints made by a graph.** `Markup.AddShape` and `Markup.AddText` stopped with "Could not locate the viewpoint ... in the tree to update its markups" on viewpoints saved by `Action.SaveViewpoint` inside a folder, so no circle or text was written. They now find the viewpoint again by its name in its folder. The markup nodes are still experimental and their coordinate scale has not been checked in Navisworks, so a circle or text may still be too small, too large or off-centre.

Nothing in this release was tried inside Navisworks; `docs/QA_CHECKLIST.md` lists what to try.

Questions and problems: support@bimcamel.com, or the issue form on GitHub. The full list is in `CHANGELOG.md`.

Only Navisworks Manage 2024 has been seen running CamelGraph in the field; 2025, 2026 and Simulate are built and installed the same way but not yet confirmed. **Help > Run Self-Test** checks the nodes against your own model.
