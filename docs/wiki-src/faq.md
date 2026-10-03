# Frequently asked questions

## The basics

### What is Dyncamelo?

A visual programming add-in for Autodesk Navisworks. You build a graph of nodes on a canvas and run it against the open model, instead of clicking through the same steps by hand or writing code. It follows the ideas Dynamo made familiar in Revit. See [Concepts](concepts.md).

### Is it free?

Free for personal and other noncommercial use: learning, hobby projects, research, charities, schools and universities, public research bodies and government institutions. Using it for your job at a company, or in a paid project or product, needs a commercial licence from BIMCamel. See [Licence](licence.md).

### Which versions of Navisworks does it support?

Navisworks **Manage** and **Simulate** **2024, 2025 and 2026** on 64-bit Windows. Other products and years are not supported. Only Manage 2024 has been seen running it in the field so far; see [Requirements](requirements.md#what-has-been-tested).

### Do I need Simulate or Manage?

Either. The clash nodes need Manage, because Simulate does not include Clash Detective.

### Do I need to know how to program?

No. A graph is built by dragging and wiring nodes. If you want a node that does not exist, you can write one as a single C# method ([Writing your own nodes](extending.md)).

### Can it open my Dynamo graphs?

No. Dyncamelo has its own graph format (`.dyc`) and its own node library, built for Navisworks. It does not run Dynamo graphs or Revit nodes.

### How many nodes are there?

More than 570, in about 46 categories, from maths and text to clash triage. Browse them in the [node reference](nodes/index.md), or press `Space` in the editor to search.

## Installing

### Where is the installer?

On the releases page and at bimcamel.com ([Installation](installation.md#where-to-get-it)). Some releases carry the source code only, with no installer; the page explains how to build and install from source.

### Do I need administrator rights?

No. The installer works per user. Only an install for all users, or into the classic Navisworks `Plugins` folder, writes somewhere that needs them.

### Windows SmartScreen warns about the installer.

The installer is not code-signed unless the publisher set a certificate up. Compare its SHA-256 checksum with the one published next to it, then choose **More info ▸ Run anyway**.

### Navisworks shows `PLUGIN_LOAD_02` when it starts.

Windows still marks the DLLs as downloaded. Run `Get-ChildItem "$env:APPDATA\Autodesk\ApplicationPlugins\Dyncamelo.bundle" -Recurse -File | Unblock-File` in PowerShell and restart Navisworks. The installer does this for you. See [Troubleshooting](troubleshooting.md#navisworks-reports-plugin_load_02-or-0x80131515-at-start).

### I cannot find the Dyncamelo button.

Look on the **BIMCamel** ribbon tab, panel **Visual Programming**. If it is not there, see [Troubleshooting](troubleshooting.md#the-bimcamel-ribbon-tab-or-the-dyncamelo-button-is-missing).

### How do I remove it?

[Uninstalling](uninstall.md). Your graphs and settings stay unless you delete them.

## Using it

### Where do I start?

[Your first script](first-steps.md) takes about ten minutes. The [sample scripts](samples.md) are on the start screen of an empty canvas.

### Does a graph change my model?

Only if it contains nodes that do. Reading nodes (search, properties, selection) do not. Writing nodes change the open document: colour, hide and transparency overrides, selection sets, viewpoints, custom properties, transforms. The description of every node in the [node reference](nodes/index.md) says what it does. The source files of the model (a Revit file, an IFC) are never modified; custom properties, for example, travel with the NWF or NWD only.

### Can I undo what a graph did?

Use the nodes made for it (`Appearance.Reset`, `Appearance.ResetAll`, `Appearance.ResetTemporary`, `Appearance.ShowAll`), or close without saving the model. Whether one Navisworks **Undo** reverses a whole run has not been confirmed, so save the model first. The editor's own undo changes the graph only. See [Running a graph](running-graphs.md#changing-the-model-and-undoing-it).

### Why did my search find nothing?

The tab and property names must be the ones Navisworks displays, including their language. Select an element in Navisworks and use the **magnifier** next to the name boxes to pick them from a list. See [Troubleshooting](troubleshooting.md#a-search-returns-nothing-or-the-magnifier-next-to-a-tab-or-property-shows-no-names).

### Why does nothing happen when I press Run?

Run only executes what changed. If nothing changed, nothing runs. Change an input, or check for frozen nodes, a false `Flow.When`, or an empty required input. See [Troubleshooting](troubleshooting.md#nothing-happens-when-i-press-run).

### What is the difference between muting and freezing a node?

A **muted** node (`M`) is bypassed: it passes its first matching input straight through and everything after it still runs. A **frozen** node (`Shift+M`) and everything after it is skipped and keeps its old results. Mute switches a step off inside a live chain; freeze stops a whole slow branch from recomputing. See [Running a graph](running-graphs.md#running-part-of-a-graph).

### What do the dashed wires mean?

A list is going into an input that expects one value, so the node runs once per item. See [Concepts](concepts.md#lists-replication-and-lacing).

### A node is red. What now?

Hover it for its message, press `I` with it selected, or open the Errors count in the status bar. See [Running a graph](running-graphs.md#node-states).

### Can one graph run for every project?

Yes. A graph stores no results and uses the active document, so open it with any model and run it. Pin down only what is specific to a project (names, levels, folders) in input nodes or in the values on the nodes.

### How do I give a graph to a colleague who does not use the editor?

Save it in `Documents\Dyncamelo\Scripts` (or a shared folder added in the Player), give it input nodes for what should change, and they run it from the [Script Player](player.md). A colleague who only needs the result can also be given the output of the graph, such as the Excel file.

### Can I run graphs automatically, without a person?

The plug-in `Dyncamelo.Run.DYNC` runs a script by path for the Navisworks Automation API and the Batch Utility, and the command-line tool runs graphs that use only the general nodes. See [Running a graph](running-graphs.md#running-without-the-editor).

### Where are my graphs and settings?

Graphs are `.dyc` files where you saved them. Settings, the error log and autosaved copies are in `%APPDATA%\Dyncamelo`. See [Privacy and safety](privacy-and-safety.md#what-it-keeps-on-your-computer).

### My graph was lost when Navisworks closed.

If **Autosave** was on (it is by default), Dyncamelo offers the autosaved copy the next time the editor opens on an empty canvas. See [Troubleshooting](troubleshooting.md#navisworks-closed-while-i-was-working).

## Safety and privacy

### Does Dyncamelo send my model or data anywhere?

No. It has no account, analytics or licence server. Its one network request is a once-a-day look for a newer version, which you can switch off. See [Privacy and safety](privacy-and-safety.md).

### Is it safe to run a graph someone sent me?

Treat a `.dyc` like a macro. A graph contains no code of its own, but its nodes can run programs, call web addresses and delete or overwrite files. Dyncamelo lists such nodes and asks before it runs a graph from a file. Look at a graph before you run it, and try it on a copy of the model. See [Privacy and safety](privacy-and-safety.md#what-a-graph-can-do).

## Help and contributing

### How do I report a bug or ask for a feature?

On GitHub: <https://github.com/mrshoma99-rgb/dyncamelo/issues>. The bug report form asks for the Dyncamelo and Navisworks versions, the end of `%APPDATA%\Dyncamelo\errors.log` and the result of **Help ▸ Copy Diagnostics**. See [Troubleshooting](troubleshooting.md#how-to-collect-diagnostics). Security problems go through the private route described in [Privacy and safety](privacy-and-safety.md#reporting-a-security-problem).

### Can I contribute nodes?

Yes. [Writing your own nodes](extending.md) describes the pack format. Pull requests are welcome; read `CONTRIBUTING.md` in the repository first.

### Is Dyncamelo connected to Autodesk?

No. It is not affiliated with or endorsed by Autodesk. Autodesk, Navisworks, Revit and Dynamo are trademarks of Autodesk, Inc.
