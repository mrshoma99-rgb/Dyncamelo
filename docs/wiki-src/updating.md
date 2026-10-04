# Updating

## How CamelGraph tells you about a new version

When the editor opens, at most once a day, CamelGraph asks GitHub for the number of the newest release. If it is newer than the one you have:

* the **start screen** (the screen on an empty canvas) shows a notice with the new version and a **Get it** button, which opens the GitHub page;
* once per version, CamelGraph also asks whether to open the download page.

Nothing is downloaded or installed by itself. You can look for an update at any time with **Help ▸ Get the Newest Version…**, which opens the releases page in your browser.

The request carries your IP address and the name `CamelGraph-UpdateCheck`, as any web request does, and nothing about you or your models. To switch it off, open **Settings ▸ Privacy** and turn off **Check for a newer version once a day**. With the check off, CamelGraph makes no network request of its own. A copy installed from the Autodesk App Store never makes this check, because the store delivers its updates. That professional copy is coming soon; the copies from GitHub and bimcamel.com are the personal-use ones ([Licence](licence.md#which-copy-do-i-need)).

![The start screen of an empty canvas, with the version next to the name and a notice about a newer version.](../images/wiki-start-screen.png)

!!! note "Updating never happens by itself"
    Nothing is downloaded or installed unless you ask. The notice only tells you that a newer version exists.

## Which version do I have?

* On the **BIMCamel** ribbon tab, click **About**: the version is at the bottom of the window.
* The start screen of an empty canvas shows it next to the name.
* **Windows Settings ▸ Apps ▸ Installed apps ▸ CamelGraph for Navisworks** shows it when you installed with the installer.
* It is the `Version` attribute in `%APPDATA%\Autodesk\ApplicationPlugins\CamelGraph.bundle\PackageContents.xml` (it reads like `0.46.0.0`).

## How to update

1. **Close Navisworks.** The files of the old version are in use while it runs.
2. Install the new version the same way you installed the old one ([Installation](installation.md)). It replaces the old files in place:
    * with the installer, run the new `CamelGraphSetup.exe`: it says "Update install (v… found)" when it finds a version already there;
    * with the batch file or a manual copy, run `install-camelgraph.bat` again or copy the new bundle over the old folder;
    * if you build from source, pull the new source and build again.
3. Start Navisworks and open CamelGraph. Check the version as above.

Your own work is not touched: your `.dyc` graphs, the scripts folder (`Documents\CamelGraph\Scripts`) and your settings in `%APPDATA%\CamelGraph` all stay.

## Updating from Dyncamelo (version 0.48 or earlier)

CamelGraph is the new name of Dyncamelo, and the files carry the new name, so the new version is a new bundle rather than the old files overwritten. The installers take care of that:

* **`CamelGraphSetup.exe`** removes the old `Dyncamelo.bundle` folder and its *Dyncamelo for Navisworks* entry in **Windows Settings ▸ Apps** before it installs the new version. Its button says "Install (replaces Dyncamelo v…)" when it finds one.
* **`install-camelgraph.bat`** does the same.
* If you copy the bundle by hand, delete `%APPDATA%\Autodesk\ApplicationPlugins\Dyncamelo.bundle` first. With both there you would get two plug-ins and two buttons.
* Your settings, recent files, favourite nodes and autosaved copies move from `%APPDATA%\Dyncamelo` to `%APPDATA%\CamelGraph` the first time the new version starts.
* Scripts in `Documents\Dyncamelo\Scripts` stay where they are. The Script Player adds that folder to its list once; take it off the list in the Player, or move the files to `Documents\CamelGraph\Scripts`.
* Your graphs open as before: the nodes they use are found under their old names.
* A node pack you built against `Dyncamelo.Core.dll` has to be built again against `CamelGraph.Core.dll` ([Extending](https://github.com/mrshoma99-rgb/dyncamelo/blob/main/docs/EXTENDING.md)).

## What changes between versions

Every release is described on [What's new](whats-new.md). Two things are worth knowing:

* **Old graphs keep working.** When a node is renamed or merged into another, the old name keeps loading in old graphs. If a node really has lost an input, the editor tells you which wire or value it dropped when you open the file ([Troubleshooting](troubleshooting.md#a-graph-opens-with-a-warning-or-with-missing-nodes)).
* **New graphs may not open in old versions.** A file made with a newer version can use nodes or a file format that an older CamelGraph does not have. It then says "The file requires .dyc reader version N but this application supports version M", or shows the missing nodes as placeholders. Update, and the file opens.

## Going back

Install the older version over the new one in the same way. Graphs that use something only the newer version has will show placeholders until you update again.

## Next steps

* [Installation](installation.md) lists the ways to install.
* [Settings](settings.md#privacy) switches the update check off.
* [Troubleshooting](troubleshooting.md#a-graph-opens-with-a-warning-or-with-missing-nodes) if an old graph opens with a warning.
