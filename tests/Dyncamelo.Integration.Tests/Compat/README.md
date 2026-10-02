# Compat: graph files saved by earlier releases

Every `.dyc` file below was shipped in a release (they are the `samples/` graphs of that release), extracted unchanged with
`git show <tag>:samples/<file>`. `OldGraphFilesTests` opens each one with today's code and fails if a node, a wire, a typed-in
value or a node's saved data no longer comes back, so a user's saved script survives an upgrade. Do not edit these files and do
not "fix" them to make a test pass: a file that stops opening means a shipped node changed incompatibly (see
`docs/EXTENDING.md`, section "Changing a node that is already shipped").

A file is stored once, under the first release tag that shipped that exact content. "Shipped unchanged" lists the tags that
carried the identical bytes, so the set covers every distinct version of every sample from `v0.9.0` (the first release with
graph files) to `v0.34.0` (the newest tag; its 17 files are all stored below under the tag that first shipped them).
Releases without a tag (for example `v0.11.x`) are not represented.

To add a new release: for each `samples/*.dyc` of the tag whose content is not stored yet, add it under `Compat/<tag>/` and a row below.

| Stored as | Shipped unchanged in |
|---|---|
| `v0.9.0/Bulk Selection Sets from Values.dyc` | v0.9.0 to v0.34.0 |
| `v0.9.0/Clash Triage and BCF Export.dyc` | v0.9.0 to v0.34.0 |
| `v0.9.0/Color Elements by Property.dyc` | v0.9.0 to v0.34.0 |
| `v0.9.0/Export Properties to Excel.dyc` | v0.9.0 to v0.34.0 |
| `v0.9.0/Getting Started - Math and Watch.dyc` | v0.9.0 to v0.34.0 |
| `v0.9.0/Isolated Viewpoints per Item.dyc` | v0.9.0 |
| `v0.9.0/QTO Rollup by Category.dyc` | v0.9.0 to v0.34.0 |
| `v0.9.0/csv-roundtrip.dyc` | v0.9.0 to v0.34.0 |
| `v0.9.0/hello-math.dyc` | v0.9.0 to v0.34.0 |
| `v0.9.0/list-lacing.dyc` | v0.9.0 to v0.34.0 |
| `v0.9.0/string-report.dyc` | v0.9.0 to v0.34.0 |
| `v0.9.1/Isolated Viewpoints per Item.dyc` | v0.9.1 to v0.34.0 |
| `v0.9.1/Spotlight Viewpoints per Item.dyc` | v0.9.1 to v0.34.0 |
| `v0.10.0/Isolated Viewpoints (Loop).dyc` | v0.10.0 to v0.19.1 |
| `v0.12.3/Floor Opening Fall-Hazard Map.dyc` | v0.12.3 to v0.13.8 |
| `v0.12.3/Floor Openings Needing Handrails.dyc` | v0.12.3 to v0.34.0 |
| `v0.14.0/Floor Opening Fall-Hazard Map.dyc` | v0.14.0 to v0.16.0 |
| `v0.17.0/Floor Opening Fall-Hazard Map.dyc` | v0.17.0 to v0.34.0 |
| `v0.19.2/Isolated Viewpoints (Loop).dyc` | v0.19.2 |
| `v0.19.3/Isolated Viewpoints (Loop).dyc` | v0.19.3 to v0.34.0 |
| `v0.25.1/Section Box Viewpoints per Group.dyc` | v0.25.1 to v0.34.0 |
| `v0.32.0/Clash Group Viewpoints per Test.dyc` | v0.32.0 to v0.33.2 |
| `v0.33.3/Clash Group Viewpoints per Test.dyc` | v0.33.3 to v0.34.0 |
