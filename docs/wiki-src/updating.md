# Updating

## How CamelGraph tells you about a new version

When the editor opens, at most once a day, CamelGraph asks GitHub for the number of the newest release. If it is newer than the one you have:

* the **start screen** (the screen on an empty canvas) shows a notice with the new version and a **Get it** button, which opens the GitHub page;
* once per version, CamelGraph also asks whether to open the download page.

Nothing is downloaded or installed by itself. You can look for an update at any time with **Help ▸ Get the Newest Version…**, which opens the releases page in your browser.

The request carries your IP address and the name `Dyncamelo-UpdateCheck`, as any web request does, and nothing about you or your models. To switch it off, open **Settings ▸ Privacy** and turn off **Check for a newer version once a day**. With the check off, CamelGraph makes no network request of its own. A copy installed from the Autodesk App Store never makes this check, because the store delivers its updates. That professional copy is coming soon; the copies from GitHub and bimcamel.com are the personal-use ones ([Licence](licence.md#which-copy-do-i-need)).

![The start screen of an empty canvas, with the version next to the name and a notice about a newer version.](../images/wiki-start-screen.png)

!!! note "Updating never happens by itself"
    Nothing is downloaded or installed unless you ask. The notice only tells you that a newer version exists.

## Which version do I have?

* On the **BIMCamel** ribbon tab, click **About**: the version is at the bottom of the window.
* The start screen of an empty canvas shows it next to the name.
* **Windows Settings ▸ Apps ▸ Installed apps ▸ Dyncamelo for Navisworks** shows it when you installed with the installer.
* It is the `Version` attribute in `%APPDATA%\Autodesk\ApplicationPlugins\Dyncamelo.bundle\PackageContents.xml` (it reads like `0.46.0.0`).

## How to update

1. **Close Navisworks.** The files of the old version are in use while it runs.
2. Install the new version the same way you installed the old one ([Installation](installation.md)). It replaces the old files in place:
    * with the installer, run the new `DyncameloSetup.exe`: it says "Update install (v… found)" when it finds a version already there;
    * with the batch file or a manual copy, run `install-dyncamelo.bat` again or copy the new bundle over the old folder;
    * if you build from source, pull the new source and build again.
3. Start Navisworks and open CamelGraph. Check the version as above.

Your own work is not touched: your `.dyc` graphs, the scripts folder (`Documents\Dyncamelo\Scripts`) and your settings in `%APPDATA%\Dyncamelo` all stay.

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
