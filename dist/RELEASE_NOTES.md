# CamelGraph v0.49.0: Dyncamelo is now CamelGraph (personal-use edition with installer)

> **Personal use only.** This download (GitHub, bimcamel.com) is the free *Personal use* edition under the PolyForm Noncommercial License 1.0.0: learning, hobby projects, research, and charities, schools, universities, public research bodies and government institutions. **For professional use** (work at a company, a consultancy or design office, a paid project or product) a copy for the **Autodesk App Store** is **coming soon**; it will come with a commercial licence from BIMCamel. It is the same program in both editions.

## What is new since 0.48.0

* **The program is now called CamelGraph** (previously Dyncamelo), with a new logo: two node ends and the wire between them. The ribbon button, the editor, the installer, the Windows Apps entry, the reports and the privacy policy carry the new name. The logo follows the theme: light on the dark palettes, dark on the Light one.
* **The files carry the new name too.** The installer is `CamelGraphSetup.exe`, the zip `CamelGraph-v0.49.0-navisworks.zip`, the bundle `CamelGraph.bundle` with `CamelGraph.App.dll`, `CamelGraph.Core.dll` and the other libraries, the script `install-camelgraph.bat`.
* **Installing replaces an old Dyncamelo install.** The installer and the batch file remove the old `Dyncamelo.bundle` folder and its entry in Windows Settings > Apps first, so you never end up with two plug-ins. The installer button says "Install (replaces Dyncamelo v…)" when it finds one.
* **Your settings, scripts and graphs carry over.** The first start moves `%APPDATA%\Dyncamelo` to `%APPDATA%\CamelGraph`, and the Script Player lists `Documents\Dyncamelo\Scripts` once so your scripts still show. Graphs saved by earlier versions open as before, and graphs saved now still open in older versions (the `.dyc` format has not changed).
* **Things that change for automation and node packs.** The Navisworks plug-in ids are `CamelGraph.Command.DYNC`, `CamelGraph.DockPane.DYNC`, `CamelGraph.Launch.DYNC`, `CamelGraph.PlayerPane.DYNC` and `CamelGraph.Run.DYNC` (the last was `Dyncamelo.Run.DYNC`). A node pack has to be built again against `CamelGraph.Core.dll`. The custom property tab that `Properties.SetCustom` writes to by default is now *CamelGraph Data* (it was *Dyncamelo Data*).

Questions and problems: support@bimcamel.com, or the issue form on GitHub. The full list is in `CHANGELOG.md`.

Only Navisworks Manage 2024 has been seen running CamelGraph in the field; 2025, 2026 and Simulate are built and installed the same way but not yet confirmed. **Help > Run Self-Test** checks the nodes against your own model.
