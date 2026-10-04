## What and why

<!-- What does this change, and why? Link the issue it closes, if there is one. Screenshots or GIFs for UI changes. -->

## Tests run

<!-- Tick what you ran, and paste anything that failed or was skipped. -->

- [ ] `dotnet test tests/CamelGraph.Core.Tests`
- [ ] `dotnet test tests/CamelGraph.Nodes.Tests`
- [ ] `dotnet test tests/CamelGraph.Integration.Tests`
- [ ] `dotnet test tests/CamelGraph.UI.Tests` (Windows only; skip for a change that touches no WPF code)
- [ ] Tried in Navisworks (year and edition: ____ ). Rows of [docs/QA_CHECKLIST.md](https://github.com/mrshoma99-rgb/dyncamelo/blob/main/docs/QA_CHECKLIST.md) that apply: ____
- [ ] Not tried in Navisworks. Say so here, and which part could not be checked: ____

## Docs and catalogue

- [ ] Docs updated (README, `docs/`, node descriptions), or no user-visible change
- [ ] Node catalogue regenerated with `python3 tools/generate_node_catalog.py` (adding, renaming or retiring a node), or no node changed
- [ ] `docs/UI_GUIDE.md` regenerated (`CAMELGRAPH_REGEN_DOCS=1 dotnet test tests/CamelGraph.Core.Tests --filter UiGuideTests`) after a change to `CommandCatalog` or `SettingsCatalog`, or no command or setting changed
- [ ] [CHANGELOG.md](https://github.com/mrshoma99-rgb/dyncamelo/blob/main/CHANGELOG.md) has an entry for a user-visible change
