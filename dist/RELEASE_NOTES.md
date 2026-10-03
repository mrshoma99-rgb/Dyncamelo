# Dyncamelo v0.47.0: personal-use edition with installer

> **Personal use only.** This download (GitHub, bimcamel.com) is the free *Personal use* edition under the PolyForm Noncommercial License 1.0.0: learning, hobby projects, research, and charities, schools, universities, public research bodies and government institutions. **For professional use** (work at a company, a consultancy or design office, a paid project or product) a copy for the **Autodesk App Store** is **coming soon**; it will come with a commercial licence from BIMCamel. It is the same program in both editions.

## What is new since 0.46.0

* **A start screen on an empty canvas.** The installed version and edition, a link to bimcamel.com, a notice with a **Get it** button when a newer version exists, and cards: **New script**, your recent scripts and the examples. The cards go as soon as you open a script or add a node.
* **A small search button next to tab and property names.** Type the name as before, or press the magnifier to list the tabs and properties of that node's own element (or, on the nodes that search the whole model, of the elements selected in Navisworks). Nothing is read until you press it.
* **Faster search nodes.** `Search.ByProperty` and friends walk the model once instead of once per data type.
* **A new Script Player layout**, and text boxes that no longer cut their text off.
* **Icons for every node library category** (Data, IFC, Report, System, Utility had none).
* **Two editions, marked as such.** The installer, the About window and the start screen say *Personal use*; **Help > Autodesk App Store…** is greyed out until the store copy exists.

The full list is in `CHANGELOG.md`.

Only Navisworks Manage 2024 has been seen running Dyncamelo in the field; 2025, 2026 and Simulate are built and installed the same way but not yet confirmed. **Help > Run Self-Test** checks the nodes against your own model.
