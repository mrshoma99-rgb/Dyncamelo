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
    └── Resources/*.png          # ribbon icons (16 px + 32 px)
```

## Install

1. Build the solution on Windows (`dotnet build Dyncamelo.sln -c Release`).
2. Copy the DLLs listed in `2024/PLACE_DYNCAMELO_DLLS_HERE.txt` into `2024/`
   (a Debug build deploys them for you — see `DeployToBundle` in
   `src/Dyncamelo.App/Dyncamelo.App.csproj`).
3. Copy `Dyncamelo.bundle/` to `%APPDATA%\Autodesk\ApplicationPlugins\`
   (or `C:\ProgramData\Autodesk\ApplicationPlugins\` for all users).
4. Start Navisworks Manage/Simulate 2024 — the **BIMCamel** tab appears with
   the Dyncamelo button; it toggles the node editor dock pane.

The classic `Plugins`-folder deployment (no ribbon tab, button under
*Tool add-ins*) still works too — see `docs/GETTING_STARTED.md`.

Per-year note: a Navisworks plug-in must be compiled against the API of the
release it runs in (2024 = API v21). A DLL built for one year loads in another
but its ribbon tab silently fails to register — future 2025/2026 support means
adding year folders here plus matching `Components` entries in
`PackageContents.xml`.
