# Dyncamelo distribution bundle

`Dyncamelo.bundle/` is the Autodesk *application bundle* used to install
Dyncamelo with its own ribbon presence — a **BIMCamel** ribbon tab with a
"Visual Programming" panel (shared with the other BIMCamel tools, whose panels
co-locate on the same tab).

```
Dyncamelo.bundle/
├── PackageContents.xml          # bundle manifest (Navisworks 2024, Manage + Simulate)
└── 2024/
    ├── Dyncamelo.App.dll        # + dependency DLLs — see PLACE_DYNCAMELO_DLLS_HERE.txt
    ├── en-US/Dyncamelo.xaml     # ribbon layout (tab "BIMCamel" → panel "Visual Programming")
    ├── Resources/*.png          # ribbon icons (16 px + 32 px)
    └── Samples/*.dyc            # example workflow graphs (in-app Samples menu)
```

## Install

Easiest: double-click **`install-dyncamelo.bat`** (next to the bundle). It
copies `Dyncamelo.bundle/` into `%APPDATA%\Autodesk\ApplicationPlugins\` for
the current user. Run `install-dyncamelo.bat uninstall` to remove it again.
The `2024/` folder in this repo already contains the compiled Release DLLs,
so no build step is needed.

Manual install / rebuilding from source:

1. Build the solution on Windows (`dotnet build Dyncamelo.sln -c Release`).
2. Copy the DLLs listed in `2024/PLACE_DYNCAMELO_DLLS_HERE.txt` into `2024/`
   (a Debug build deploys them for you — see `DeployToBundle` in
   `src/Dyncamelo.App/Dyncamelo.App.csproj`).
3. Copy `Dyncamelo.bundle/` to `%APPDATA%\Autodesk\ApplicationPlugins\`
   (or `C:\ProgramData\Autodesk\ApplicationPlugins\` for all users).
4. Start Navisworks Manage/Simulate 2024 — the **BIMCamel** tab appears with
   the Dyncamelo button; it toggles the node editor dock pane.

## Example workflows (Samples)

The `2024/Samples/` folder ships six ready-made `.dyc` example graphs —
open them from Dyncamelo's **Samples** menu (or via File → Open). *Getting
Started - Math and Watch* runs without a model; the others (color by
property, Excel property export, bulk selection sets, clash triage + BCF
export, QTO rollup) expect an open Navisworks model and explain their
editable inputs in notes on the canvas. When building from source, the
`LayoutNavisworksPlugin` / `DeployToBundle` targets stage them automatically
from the repository's `samples/` directory.

## Troubleshooting

**`PLUGIN_LOAD_02` / `FileLoadException 0x80131515` ("Operation is not
supported") on Navisworks start** — Windows kept the browser's "downloaded
file" mark (Zone.Identifier) on the DLLs; .NET Framework refuses to load
web-marked assemblies. `install-dyncamelo.bat` strips the mark automatically;
for a manual install run in PowerShell:

```powershell
Get-ChildItem "$env:APPDATA\Autodesk\ApplicationPlugins\Dyncamelo.bundle" -Recurse -File | Unblock-File
```

(or unblock the downloaded zip *before* extracting: right-click → Properties →
Unblock), then restart Navisworks.

The classic `Plugins`-folder deployment (no ribbon tab, button under
*Tool add-ins*) still works too — see `docs/GETTING_STARTED.md`.

Per-year note: a Navisworks plug-in must be compiled against the API of the
release it runs in (2024 = API v21). A DLL built for one year loads in another
but its ribbon tab silently fails to register — future 2025/2026 support means
adding year folders here plus matching `Components` entries in
`PackageContents.xml`.
