# Installation

Dyncamelo installs as an Autodesk **application bundle**: a folder that Navisworks finds by itself at start-up and that gives you a **BIMCamel** ribbon tab. It works per user and needs no administrator rights.

Before you begin, check the [requirements](requirements.md), and close Navisworks.

## Where to get it

* The **releases page** on GitHub: <https://github.com/mrshoma99-rgb/dyncamelo/releases/latest>.
* The Dyncamelo page on bimcamel.com: <https://www.bimcamel.com/plugins/dyncamelo>.

Download Dyncamelo only from those two places. A release that carries the ready-to-install files has these downloads next to its source code:

| File | What it is |
|---|---|
| `DyncameloSetup.exe` | The graphical installer. The whole bundle is inside it. |
| `Dyncamelo-<version>-navisworks.zip` | The bundle folder, the installer, `install-dyncamelo.bat`, the licence and a short readme. |
| `….sha256` files | A checksum for each of the two downloads above. |

**Some releases carry the source code only**, with no installer and no zip (the newest ones at the time of writing). In that case, use [Build it from source](#build-it-from-source) below. The assets list of the release page tells you which kind you are looking at.

## Option A: the installer (recommended)

1. Run `DyncameloSetup.exe`. Its first screen says which supported Navisworks years it found.
2. Click **Install**. The installer extracts the bundle to `%APPDATA%\Autodesk\ApplicationPlugins\Dyncamelo.bundle`, adds an entry to **Windows Settings ▸ Apps** and shows **Installed.** when it is done. If something fails, it says "Setup could not finish" and offers **Try again**.
3. Start Navisworks.

Details:

* If a version is already installed, the installer says "Update install (v… found)" and replaces it ([Updating](updating.md)).
* For a silent install, for example from a deployment tool: `DyncameloSetup.exe /silent`.
* The files the installer writes carry no "downloaded from the internet" mark, so the `PLUGIN_LOAD_02` failure described below cannot happen.
* The installer is **not code-signed** unless the publisher set a certificate up, so Windows SmartScreen may show a warning. Choose **More info**, then **Run anyway**. To check what you downloaded, compare its checksum with the `.sha256` file of the release:

```powershell
Get-FileHash .\DyncameloSetup.exe -Algorithm SHA256
```

## Option B: the zip and `install-dyncamelo.bat`

1. Extract the zip anywhere.
2. Run `install-dyncamelo.bat`. It copies `Dyncamelo.bundle` into `%APPDATA%\Autodesk\ApplicationPlugins\` and removes the "downloaded file" mark from every file it copied.
3. Start Navisworks.

`install-dyncamelo.bat uninstall` removes it again ([Uninstalling](uninstall.md)).

## Option C: copy the bundle by hand

1. Take the `Dyncamelo.bundle` folder (from the zip, or from your own build).
2. Copy it to one of these:
    * `%APPDATA%\Autodesk\ApplicationPlugins\` for **you only**, or
    * `C:\ProgramData\Autodesk\ApplicationPlugins\` for **all users** (needs administrator rights).
3. **Unblock the files.** Windows marks files from a downloaded zip, and .NET Framework refuses to load such DLLs. In PowerShell:

```powershell
Get-ChildItem "$env:APPDATA\Autodesk\ApplicationPlugins\Dyncamelo.bundle" -Recurse -File | Unblock-File
```

You can instead unblock the zip *before* you extract it: right-click it, **Properties**, tick **Unblock**.

4. Start Navisworks.

The bundle must look like this. `PackageContents.xml` tells Navisworks which folder belongs to which year:

```
Dyncamelo.bundle\
    PackageContents.xml
    2024\   Dyncamelo.App.dll, the other Dyncamelo DLLs, en-US\Dyncamelo.xaml, Resources\, Samples\
    2025\   (the same, built for Navisworks 2025)
    2026\   (the same, built for Navisworks 2026)
```

Each year has its own folder because a Navisworks plug-in must be built for the release it runs in. A DLL built for one year still loads its ribbon button in another, but its dock pane cannot register.

## Option D: the classic Plugins folder (no ribbon tab)

Use this only if you cannot use an application bundle. There is then no **BIMCamel** tab; the button is under **Tool add-ins**.

1. Close Navisworks.
2. Create a folder named exactly like the plug-in DLL inside the Navisworks `Plugins` folder, for example `C:\Program Files\Autodesk\Navisworks Manage 2024\Plugins\Dyncamelo.App\` (use the Simulate and year folder that matches your product). This needs administrator rights.
3. Copy the Dyncamelo files into it: `Dyncamelo.App.dll`, `Dyncamelo.UI.dll`, `Dyncamelo.Navisworks.dll`, `Dyncamelo.Nodes.dll`, `Dyncamelo.Core.dll`, `Nodify.dll`, `Newtonsoft.Json.dll`, `AutomaticGraphLayout.dll`, `en-US\Dyncamelo.xaml` and the `Resources\*.png` files.
4. Unblock the files as in Option C.
5. Start Navisworks.

## Build it from source

You need Windows and the tools on the [requirements](requirements.md#to-build-it-yourself) page.

```powershell
git clone https://github.com/mrshoma99-rgb/dyncamelo.git
cd dyncamelo
dotnet build Dyncamelo.sln -c Release
```

A **Debug** build of the solution also places the whole bundle in `%APPDATA%\Autodesk\ApplicationPlugins\Dyncamelo.bundle` for you. To install a Release build, follow Option C with the files the build produced (the list is in `dist\Dyncamelo.bundle\2024\PLACE_DYNCAMELO_DLLS_HERE.txt`).

You can also run graphs that use only the general nodes, with no Navisworks, from the cross-platform command line tool: `dotnet run --project src/Dyncamelo.Cli -- run samples/hello-math.dyc`.

## Check that it works

1. Start Navisworks and **open a model**. Dyncamelo works against the active document.
2. Look for the **BIMCamel** ribbon tab, panel **Visual Programming**. It has three buttons:
    * **Dyncamelo** opens the node editor as a dockable pane.
    * **Player** opens the [Script Player](player.md).
    * **About** shows the version and the BIMCamel links.
3. Click **Dyncamelo**. The editor opens on an empty canvas with a start screen: the version, cards for a new script, your recent scripts and the examples. Dock the pane, float it, or move it to a second monitor; Navisworks remembers where you put it.
4. Open the **Getting Started - Math and Watch** example from the start screen. It needs no model. Press **Run** (`F5`): the Watch nodes show **32** and **Area = 32**.
5. For a deeper check with your model open, choose **Help ▸ Run Self-Test…**. It runs a set of read-only Navisworks nodes and shows pass or fail for each.

Then continue with [Your first script](first-steps.md).

## If something goes wrong

| Symptom | Where to look |
|---|---|
| Navisworks shows `PLUGIN_LOAD_02` or `0x80131515` when it starts | The files are still marked as downloaded. Unblock them as in Option C. See [Troubleshooting](troubleshooting.md#navisworks-reports-plugin_load_02-or-0x80131515-at-start). |
| No **BIMCamel** tab, or no Dyncamelo button | [Troubleshooting](troubleshooting.md#the-bimcamel-ribbon-tab-or-the-dyncamelo-button-is-missing): wrong folder, a Navisworks that has not been restarted, a year that is not listed, blocked files. |
| The button says "The Dyncamelo editor panel is not registered with Navisworks" | A DLL built for another Navisworks year. Reinstall with files that carry a build for your year. |
| SmartScreen warns about the installer | The installer is not code-signed. **More info ▸ Run anyway**, after checking the checksum. |
