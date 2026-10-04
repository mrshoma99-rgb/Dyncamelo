# CamelGraph privacy policy

*Applies to CamelGraph (previously Dyncamelo) for Autodesk Navisworks, from version 0.45 on. Published by BIMCamel. The same text is inside the app: **Help > Privacy Policy**.*

## In short

CamelGraph does not collect your data. It has no account, no sign-in, no licence server, no analytics and no crash reporting that leaves your computer. Your models and your graphs stay on your computer unless a graph you run sends them somewhere (see "What a graph can send").

## What CamelGraph stores on your computer

Everything below is in the folder `%APPDATA%\Dyncamelo` (on a standard Windows install, `C:\Users\<you>\AppData\Roaming\Dyncamelo`). None of it is sent anywhere by CamelGraph.

* `ui-settings.json`: your preferences (palette, shortcuts, recent files and favourite nodes, Script Player folders and the last values you typed into scripts, which graph files you agreed to run).
* `errors.log`: the technical details of a failure that CamelGraph caught: the error, the place in the code, the XAML file involved. An error message can name a node or a file; model contents are not written to it.
* `update-check.txt`: the date of the last update check and the last version offered.
* `recovery` (a folder): copies of graphs with unsaved changes, so they can be offered back after a crash. Deleted when you save.

**Help > Copy Diagnostics** and **Help > Run Self-Test** put a text report on the clipboard when you ask. Your user name, computer name and profile folder are replaced in it, and no model or graph names are included. CamelGraph does not send the report anywhere; you decide whether to paste it into a bug report.

## What CamelGraph sends over the network

One thing, and you can switch it off.

* **Update check.** When the editor opens, at most once a day, CamelGraph asks GitHub (`https://api.github.com/repos/mrshoma99-rgb/dyncamelo/releases/latest`) for the number of the newest release. The request carries your IP address and the name `Dyncamelo-UpdateCheck`, as any web request does; nothing else about you or your models. If a newer version exists, CamelGraph asks before it opens the download page. GitHub handles the request under its privacy statement (https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). **Switch it off:** Settings > Privacy > "Check for a newer version once a day". When it is off, CamelGraph makes no network request of its own. A copy installed from the Autodesk App Store never makes this check: the store delivers its updates.

Opening the user guide, the website or a download page from a menu or a link opens your web browser at your request; that is then between you and that website.

## What a graph can send

A graph is a program, and some nodes reach outside Navisworks: `Web.Get` and `Web.Post` call a web address, `System.Run` starts a program, and the file nodes read and write files. A graph that uses them can send data, including model data, to wherever its author pointed it. CamelGraph asks before it runs a graph from a file that contains such nodes (Settings > Editing > "Ask before running graphs from files"), but what a graph does when you let it run is up to the graph. Only run graphs you trust. See SECURITY.md in the project repository.

## Third parties

CamelGraph contains no advertising, analytics or tracking libraries and shares your data with no third party. Its libraries (Nodify, Newtonsoft.Json, AutomaticGraphLayout and others, listed in THIRD-PARTY-NOTICES.md) run inside Navisworks and contain no code that contacts the network. If you download CamelGraph from the Autodesk App Store, Autodesk handles the download and may tell the publisher that the app was downloaded; that is covered by Autodesk's own terms.

## Keeping and deleting data

CamelGraph keeps nothing on any server, so there is nothing to delete elsewhere. To remove what it stored on your computer, close Navisworks and delete the folder `%APPDATA%\Dyncamelo`. The uninstaller removes the program but leaves that folder and your `.dyc` graphs alone, so you can keep them.

## Withdrawing consent and asking questions

The only optional network request is the update check; switch it off as described above. If you have a question about this policy, open an issue at https://github.com/mrshoma99-rgb/dyncamelo/issues or use the support contact on the app's listing. If the policy changes, the change is described in the release notes of the version that changes it.
