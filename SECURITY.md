# Security policy

This page says how to report a security problem in Dyncamelo, which versions get fixes, and what a `.dyc` graph file can do on your computer. The last part matters most for everyday use: **a graph is a program. Only run graphs you trust.**

## Reporting a vulnerability

Use GitHub's private security advisories:

1. Open the repository's **Security** tab, then **Report a vulnerability**. The direct link is
   <https://github.com/mrshoma99-rgb/dyncamelo/security/advisories/new>.
2. Describe what you found, which Dyncamelo version and Navisworks year it affects, and how to reproduce it. A small `.dyc` file that shows the problem helps a lot.
3. Please do not open a public issue or discussion for a vulnerability, and do not post the details anywhere until it is fixed.

This is a small project. We read every report and answer in the advisory thread, but we cannot promise a response time.

What counts as a vulnerability:

* A `.dyc` file that makes Dyncamelo do something harmful **without** any of the nodes listed under [What a graph file can do](#what-a-graph-file-can-do).
* A way round the questions Dyncamelo asks before it runs those nodes.
* The installer writing outside its own folder, or running a file it should not.

What does not count: a graph doing what its nodes say, after you chose to run it. `System.Run` runs a program because that is what it is for. See the advice below.

## Supported versions

Only the **latest release** gets security fixes. If you use an older version, update first and check whether the problem is still there.

| Version | Supported |
|---|---|
| Latest release (see [Releases](https://github.com/mrshoma99-rgb/dyncamelo/releases/latest)) | Yes |
| Any older release | No |

Dyncamelo supports Autodesk Navisworks 2024, 2025 and 2026 on Windows.

## What a graph file can do

A `.dyc` file is JSON. It holds nodes, the wires between them and the values typed into them. It contains no program code of its own, and there is no node that runs Python or C# code you type in. (`Math.Formula` and `Table.AddFormulaColumn` use a small arithmetic parser.)

But the nodes themselves do real work when the graph runs, and a graph can use any of them. These groups matter for security. All names below are nodes in the library (see the [node catalogue](docs/NODE_CATALOG.md)).

| A graph can... | Through these nodes |
|---|---|
| **Run programs** | `System.Run` starts any program with any arguments and returns what it prints. `System.OpenPath` opens a file or folder with its default application, which can start a program. |
| **Call web addresses** | `Web.Get` downloads from an http or https address. `Web.Post` sends data to one (a web hook, for example). |
| **Delete, move and overwrite files** | `File.Delete`, `File.Move`, `File.Copy`, `Directory.Delete`, `Directory.Create`, `Zip.Extract`, `Zip.Create` |
| **Write files** | `Text.WriteToFile`, `Text.AppendToFile`, `CSV.WriteToFile`, `CSV.AppendToFile`, `JSON.WriteToFile`, `Excel.WriteToFile`, `Table.ToCsvFile`, `Table.ToExcelFile`, `Log.Write` |
| **Read files and facts about your computer** | `Text.ReadFromFile`, `CSV.ReadFromFile`, `Excel.ReadFromFile`, `JSON.ReadFromFile`, `Directory.FindFiles`, `System.Environment` (user name, computer name and well-known folders). Reading nodes are not marked as risky, but a graph can read a file and send it away with `Web.Post`. |
| **Change the open model or the files Navisworks writes** | The Navisworks nodes that change the document, for example `Appearance.OverrideColor`, `Appearance.Hide`, `SelectionSet.Create`, `Properties.SetCustom`, `ModelItem.Translate`, `Document.Open`, `Document.Save`, `Model.Remove`, `Export.NWD`, `Export.ToIfc`, `BCF.ExportIssues`. |

`Zip.Extract` refuses archive entries that would land outside the target folder.

Three more things to know:

* **Opening a graph can run it.** A graph saved with run mode **Automatic** starts running as soon as it is opened in the editor. Sample graphs such as *Getting Started - Math and Watch* are saved that way. This is the reason for the editor warning described below.
* **Node packs are code.** Dyncamelo loads every `.dll` it finds in a `Packages` folder next to `Dyncamelo.App.dll` (subfolders included) when it builds the node library, the first time the editor or the Player opens in a session. The code then runs inside Navisworks with your rights. A pack can do anything a Navisworks add-in can. Install packs only from authors you trust.
* **The command-line tool does not ask.** `dotnet run --project src/Dyncamelo.Cli -- run <file>` (built from source, not part of the installer) runs a graph without any question. It cannot use the Navisworks nodes.

### What Dyncamelo asks before running a graph

* **Script Player.** A script that contains nodes marked as changing things (everything in the table above except `Web.Get` and the reading nodes) is listed by name above its form. The first time you run it, and again whenever the file changes, the Player asks you to confirm. Scripts that only read are never asked about. The answer is remembered per file version. `Web.Get` is a reading node, so a script whose only risky node is `Web.Get` does **not** trigger the question. The `Dyncamelo.Run.DYNC` plug-in, which runs a script by path for the Navisworks Automation API and the Batch Utility, applies the same question.
* **Editor warning before running a graph opened from a file.** New in 1.0. When you open a graph from a file that contains nodes that run programs, use the network, or delete or move files, the editor warns you before it runs, and offers to trust that file until it changes.
* A muted or frozen node is not counted when the Player decides whether to ask.

### Advice

* **Only run graphs from sources you trust**, the same way you would treat a macro or a script.
* Before you run a graph from someone else, look at it. Use **Find Node on Canvas** (`Ctrl+F`) to search for `System`, `Web`, `File`, `Directory` and `Zip`. Check what the Navisworks nodes in it will change.
* Try a graph you do not know on a copy of the model, with a normal user account, not on a machine that holds anything you cannot afford to lose.
* Say no to the Player's or the editor's question if you are not sure what the listed nodes will do.
* Do not run `.dyc` files from e-mail attachments or unknown downloads without reading them first.

## What Dyncamelo sends over the network

Searched in the source: besides the `Web.Get` and `Web.Post` nodes, the only network call is the **update check**. Once a day, when the editor pane opens, Dyncamelo asks `https://api.github.com/repos/mrshoma99-rgb/dyncamelo/releases/latest` for the newest release number. It sends no information about you or your models beyond what any web request carries (the request has the user agent `Dyncamelo-UpdateCheck`). If a newer version exists, it asks you before it opens the download page. The date of the last check is kept in `%APPDATA%\Dyncamelo\update-check.txt`. There is no setting to switch the check off.

## Downloads

`DyncameloSetup.exe` is **not code-signed**, so Windows SmartScreen shows a warning (**More info**, then **Run anyway**). Each release publishes a SHA-256 checksum file next to every download (`DyncameloSetup.exe.sha256` and the zip's `.sha256`). To check a download, run this in PowerShell and compare the result with the checksum file:

```powershell
Get-FileHash .\DyncameloSetup.exe -Algorithm SHA256
```

Only download Dyncamelo from this repository's [Releases](https://github.com/mrshoma99-rgb/dyncamelo/releases) page or from [bimcamel.com](https://www.bimcamel.com/plugins/dyncamelo).

The installer works per user, in `%APPDATA%\Autodesk\ApplicationPlugins\Dyncamelo.bundle`, and needs no administrator rights.
