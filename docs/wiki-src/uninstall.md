# Uninstalling

Close Navisworks first. The setup window warns you if it is still running.

## If you installed with the installer

Any one of these:

* **Windows Settings ▸ Apps ▸ Installed apps ▸ Dyncamelo for Navisworks ▸ Uninstall**.
* Run `DyncameloSetup.exe` and choose **Remove existing install**.
* Silently, from a command prompt: `DyncameloSetup.exe /uninstall /silent`.

## If you installed with `install-dyncamelo.bat`

From the folder that holds the batch file, run:

```
install-dyncamelo.bat uninstall
```

## If you copied the bundle by hand

Delete the folder `Dyncamelo.bundle` from the place you put it:

* `%APPDATA%\Autodesk\ApplicationPlugins\Dyncamelo.bundle` (this user only), or
* `C:\ProgramData\Autodesk\ApplicationPlugins\Dyncamelo.bundle` (all users, needs administrator rights).

## If you used the classic Plugins folder

Delete the folder `Dyncamelo.App` from `…\Navisworks Manage 2024\Plugins\` (or the Simulate and year folder you used). This needs administrator rights.

## What stays behind, and how to remove it

Uninstalling removes the program and the Apps entry. It deliberately **leaves your own files**, so you can reinstall later and carry on:

| Left behind | Where | What it is |
|---|---|---|
| Your graphs | wherever you saved them | Your `.dyc` files. |
| Your scripts folder | `Documents\Dyncamelo\Scripts` | What the Script Player lists. |
| Settings and logs | `%APPDATA%\Dyncamelo` | `ui-settings.json` (preferences, shortcuts, recent files, favourite nodes, values typed into scripts), `errors.log`, `update-check.txt` and the `recovery` folder of autosaved graphs. |

For a completely clean start, delete the `%APPDATA%\Dyncamelo` folder yourself (paste that into the Windows Explorer address bar). Dyncamelo keeps nothing on any server, so there is nothing to delete elsewhere ([Privacy and safety](privacy-and-safety.md)).

## Check that it is gone

Start Navisworks. The **BIMCamel** tab should no longer have Dyncamelo, Player and About buttons from this install. If another BIMCamel tool is installed, its own buttons stay.

If the tab or buttons are still there, look for a second copy: in the other `ApplicationPlugins` folder (user and all-users), or in a `Plugins\Dyncamelo.App` folder of Navisworks.
