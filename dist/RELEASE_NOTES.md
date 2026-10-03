# Dyncamelo v0.48.0: personal-use edition with installer

> **Personal use only.** This download (GitHub, bimcamel.com) is the free *Personal use* edition under the PolyForm Noncommercial License 1.0.0: learning, hobby projects, research, and charities, schools, universities, public research bodies and government institutions. **For professional use** (work at a company, a consultancy or design office, a paid project or product) a copy for the **Autodesk App Store** is **coming soon**; it will come with a commercial licence from BIMCamel. It is the same program in both editions.

## What is new since 0.47.0

* **One header row.** The blue brand bar and the menu bar are merged: the BIMCamel logo and name, the menus, the name of the open script and the buttons share a single row, so the canvas gains a row. In a narrow pane the script name goes first, then the name text, and in a very narrow pane the menus fold into one **☰** button; the buttons always stay.
* **A wide, solid Run button** in the palette's main-action colour, with the **Auto / Manual** switch beside it (the on/off switch of the Boolean nodes).
* **Three clash grouping nodes that did nothing now work.** `Clash.GroupResultsBySameItem`, `Clash.GroupResultsByProximity` and `Clash.GroupResultsByLevel` reported success but left Clash Detective unchanged; they now change the clash tree like the other grouping nodes. A graph that reuses `ClashTest.Results` after one of them has to take the results again (the node's message says so).
* **Faster on big models and big tables:** `Clash.GroupResults` and `GroupResultsByProximity`, `Viewpoints.FromClashResults`, `SelectionSets.BulkByPropertyValues`, `Selection.Remove`, `BCF.ImportIssues`, `Proximity.NearestDistance`, `Table.Sort` (a column that mixes numbers and text), and the `Dictionary` lookups. The results are the same as before; the logic was tested against the old code, but the speed-ups have not been timed in Navisworks yet.

The full list is in `CHANGELOG.md`.

Only Navisworks Manage 2024 has been seen running Dyncamelo in the field; 2025, 2026 and Simulate are built and installed the same way but not yet confirmed. **Help > Run Self-Test** checks the nodes against your own model.
