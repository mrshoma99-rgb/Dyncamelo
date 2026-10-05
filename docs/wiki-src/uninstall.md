# Uninstalling

Close Navisworks first. The setup window warns you if it is still running.

!!! tip "Your work is safe"
    Uninstalling removes the program and the Apps entry. Your `.dyc` graphs, your scripts folder and your settings stay where they are, so you can reinstall later and carry on. The table [below](#what-stays-behind-and-how-to-remove-it) lists them.

## If you installed with the installer

Any one of these:

* **Windows Settings ▸ Apps ▸ Installed apps ▸ CamelGraph for Navisworks ▸ Uninstall**.
* Run `CamelGraphSetup.exe` and choose **Remove existing install**.
* Silently, from a command prompt: `CamelGraphSetup.exe /uninstall /silent`.

## If you got the professional copy from the Autodesk App Store

That copy is coming soon. Remove it the way the store describes. What stays behind is the same as in the table [below](#what-stays-behind-and-how-to-remove-it).

## If you installed with `install-camelgraph.bat`

From the folder that holds the batch file, run:

```
install-camelgraph.bat uninstall
```

Uninstalling also removes a leftover install from the time the product was called Dyncamelo (`Dyncamelo.bundle` and its Apps entry), if there is one.

## If you copied the bundle by hand

Delete the folder `CamelGraph.bundle` from the place you put it:

* `%APPDATA%\Autodesk\ApplicationPlugins\CamelGraph.bundle` (this user only; the old name of the folder was `Dyncamelo.bundle`), or
* `C:\ProgramData\Autodesk\ApplicationPlugins\CamelGraph.bundle` (all users, needs administrator rights).

## If you used the classic Plugins folder

Delete the folder `CamelGraph.App` from `…\Navisworks Manage 2024\Plugins\` (or the Simulate and year folder you used). This needs administrator rights.

## What stays behind, and how to remove it

Uninstalling removes the program and the Apps entry. It deliberately **leaves your own files**, so you can reinstall later and carry on:

| Left behind | Where | What it is |
|---|---|---|
| Your graphs | wherever you saved them | Your `.dyc` files. |
| Your scripts folder | `Documents\CamelGraph\Scripts` | What the Script Player lists. |
| Your node packs | `%APPDATA%\CamelGraph\Packages` | The packs of other people's or your own nodes ([Writing your own nodes](extending.md)). |
| Settings and logs | `%APPDATA%\CamelGraph` | `ui-settings.json` (preferences, shortcuts, recent files, favourite nodes, values typed into scripts), `errors.log`, `update-check.txt` and the `recovery` folder of autosaved graphs. |

For a completely clean start, delete the `%APPDATA%\CamelGraph` folder yourself (paste that into the Windows Explorer address bar). That also removes your node packs, so keep a copy of any you want. CamelGraph keeps nothing on any server, so there is nothing to delete elsewhere ([Privacy and safety](privacy-and-safety.md)).

## Check that it is gone

Start Navisworks. The **BIMCamel** tab should no longer have CamelGraph, Player and About buttons from this install. If another BIMCamel tool is installed, its own buttons stay.

If the tab or buttons are still there, look for a second copy: in the other `ApplicationPlugins` folder (user and all-users), or in a `Plugins\CamelGraph.App` folder of Navisworks.

## Next steps

* [Installation](installation.md) to put it back, and [Updating](updating.md) if you only want a newer version.
* [Privacy and safety](privacy-and-safety.md#removing-what-it-stored) for what is stored on your computer.
