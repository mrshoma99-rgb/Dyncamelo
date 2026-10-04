# Glossary

The words used in CamelGraph and in this guide, in alphabetical order. Each entry links to the page that explains it. If a word is about the Navisworks model rather than about CamelGraph, the entry says so.

## A to B

Action
:   A step built by an `Action.*` node, such as `Action.Isolate`, `Action.ZoomTo` or `Action.SaveViewpoint`. `Workflow.ForEach` plays a list of actions for each item, one item completely before the next. See [Save one viewpoint for every item](howto/viewpoint-per-item.md). Action is also a socket [kind](ports-and-kinds.md#kind-the-colour-and-letter-of-a-socket).

Active document
:   The Navisworks document that is open. A **Document** input with nothing wired to it uses it, so the same graph works on whatever model you have open. See [Concepts](concepts.md#document).

Appearance override
:   A change to how items look in Navisworks: a colour, a transparency or a hide. A *permanent* override is saved with the file and removed with `Appearance.Reset` or `Appearance.ResetAll`. A *temporary* override lasts until you reset it or close the view. See [Running a graph](running-graphs.md#changing-the-model-and-undoing-it).

Auto and Manual
:   The two run modes in the run bar. In **Manual** you press **Run** (++f5++). In **Auto** a run starts after every edit. See [Running a graph](running-graphs.md#run-auto-and-manual).

Autosave
:   A safety copy of a script with unsaved changes, written about once a minute to `%APPDATA%\Dyncamelo\recovery` and offered back after a crash. See [Saving and opening](saving-opening.md#autosave-and-recovery).

BCF
:   BIM Collaboration Format: the vendor-neutral file for issues, a `.bcfzip` of topics with a title, status, comments, a camera and the elements involved. See [Send clashes to BCF](howto/clash-issues-bcf.md) and [IFC, BCF, Excel and CSV](exchange-formats.md#bcf-issues).

Bookmark
:   A named canvas position and zoom, saved in the script file (++ctrl+k++). See [The editor](canvas-and-nodes.md#bookmarks).

Breadcrumb bar
:   The bar at the top left of the canvas while a node group is open, such as `My script ▸ Level colours`. See [Node groups](node-groups.md#open-a-group-and-get-back-out).

## C to D

Canvas
:   The middle of the editor, where nodes and wires live. See [The editor](canvas-and-nodes.md#the-canvas).

Category
:   Two meanings. In the **property** sense it is a tab of the Navisworks Properties window, such as *Element* or *Item*; nodes ask for it as `categoryName`. In the **library** sense it is a folder of nodes. See [Find the name of a tab or property](howto/find-property-names.md) and [Node library and search](library-and-search.md#categories).

Choice
:   An input node that offers a pick-list of your own options, one per line. It gives the chosen text and its position. In the Script Player it is a drop-down. See [Give colleagues a form](howto/form-for-colleagues.md).

Clash test
:   A Clash Detective test in Navisworks Manage. CamelGraph reads and runs tests and their results with the `Clash*` nodes. See [Make a clash report](howto/clash-report.md).

Command palette
:   A search box for every command, setting and canvas node, opened with ++ctrl+shift+p++. See [The editor](canvas-and-nodes.md#the-command-palette).

Cross-Product
:   A [lacing](#l-to-m) mode that pairs every item of one list with every item of the other, giving a list of lists. See [Concepts](concepts.md#lists-replication-and-lacing).

Dashed wire
:   A wire that feeds a list into an input that wants one value, so the node runs once for each item. See [Inputs, outputs and kinds](ports-and-kinds.md#shape-one-value-or-a-list).

Default
:   The value an optional input uses when nothing is wired and nothing is typed. It is shown in the socket tooltip and in the [node library](nodes/index.md). See [Inputs, outputs and kinds](ports-and-kinds.md#giving-an-input-a-value).

Document
:   A Navisworks document, and the socket [kind](ports-and-kinds.md#kind-the-colour-and-letter-of-a-socket) for it (`F`). See [Concepts](concepts.md#document).

`.dyc`
:   The file a graph is saved in: small, versioned, plain text (JSON). It stores nodes, wires and typed values, not results and not your model. See [Saving and opening](saving-opening.md#what-a-dyc-file-is).

## E to G

Error
:   The red node state: the node failed, its outputs are empty and nodes after it wait. See [Read errors and warnings](howto/read-errors-and-warnings.md) and [Running a graph](running-graphs.md#node-states).

Executed
:   The node state of a node that ran fine.

Favourites
:   Nodes you starred in the library. They are pinned in a **★ Favourites** folder and come first in searches. See [Node library and search](library-and-search.md#favourites).

Frame
:   A coloured rectangle behind some nodes, made with ++ctrl+g++. It is for tidiness only and is not a [node group](#n-to-o). See [The editor](canvas-and-nodes.md#notes-and-frames).

Freeze
:   Hold a node, and everything after it, at the results of its last run (++shift+m++, badge FROZEN). See [Running a graph](running-graphs.md#running-part-of-a-graph) and [Speed up a slow graph](howto/speed-up-slow-graph.md).

Graph
:   What you build on the canvas: nodes joined by wires. Saved to disk it is a `.dyc` file. See [Concepts](concepts.md#graph-script-node-wire).

Group Input and Group Output
:   The two special nodes inside an open node group. Their sockets are the group's inputs and outputs. See [Node groups](node-groups.md#edit-the-interface).

## I to K

IFC GlobalId
:   The 22-character identity of an IFC element, such as `0$WU4A9R19$vKWO$AdOnKA`. Other tools show it as a standard GUID. `ModelItem.IfcGuid`, `IFC.GuidDecode` and `IFC.GuidEncode` read and convert it. See [IFC, BCF, Excel and CSV](exchange-formats.md#ifc-identity-globalids).

Idle
:   The node state of a node that has not run, or cannot run yet: a required input is empty, or a `Flow.When` before it was false. It is not an error. See [Running a graph](running-graphs.md#node-states).

Input node
:   A node in the *Input* category that holds a value you type or pick: `Number`, `Integer`, `Number Slider`, `Integer Slider`, `Boolean`, `String`, `Date`, `Choice`, `File Path`, `Directory Path` and `Color Picker`. The Script Player turns them into form fields. See [The Script Player](player.md#the-form).

Instance (node group)
:   One use of a node group on the canvas. It looks like a normal node whose sockets are the group's interface. Editing the group changes every instance. See [Node groups](node-groups.md#the-pieces).

Instance GUID
:   The stable GUID of a model item, read by `ModelItem.InstanceGuid` and matched by `Search.ByGuid`. See [Find elements from a list of GUIDs](howto/find-elements-from-guids.md).

Interface (node group)
:   The list of inputs and outputs of a node group. See [Node groups](node-groups.md#the-pieces).

Item
:   A Navisworks model item (`ModelItem`): a thing in the model tree such as a wall or a pipe. Not to be confused with a [node](#n-to-o). See [Concepts](concepts.md#graph-script-node-wire).

Kind
:   The type of data a socket carries (number, text, item, colour and so on), shown by its colour and, with **Type letters in sockets** on, by a letter. See [Inputs, outputs and kinds](ports-and-kinds.md#kind-the-colour-and-letter-of-a-socket).

## L to M

Lacing
:   How two lists pair up when both go into one-value inputs: **Shortest** (the default), **Longest** or **Cross-Product**. Set it from the node's right-click menu. See [Concepts](concepts.md#lists-replication-and-lacing).

Library
:   The panel on the left of the canvas that lists every node in a category tree, with a search box. See [Node library and search](library-and-search.md).

List level (`@L`)
:   A setting on an input that says which depth of a nested list it takes: `@L1` is the individual items, `@L2` the lists of items. It also makes an input of kind *Any* run once for each item. See [Concepts](concepts.md#lists-replication-and-lacing) and [Write spreadsheet data onto model items](howto/write-excel-data-onto-items.md).

Loop
:   Everything wired between `Loop.Item` and `Loop.Collect` runs once for each item, in order. See [Save one viewpoint for every item](howto/viewpoint-per-item.md).

Magnifier
:   The small search button next to a tab or property box. It lists names from one element. See [Find the name of a tab or property](howto/find-property-names.md).

Minimap
:   A small overview of the canvas in the bottom right corner (++ctrl+m++). See [The editor](canvas-and-nodes.md#the-canvas).

ModelItem
:   See [Item](#i-to-k).

Multi-input
:   A pill-shaped socket that takes any number of wires and combines them into one list. See [Inputs, outputs and kinds](ports-and-kinds.md#shape-one-value-or-a-list).

Mute
:   Bypass a node (++m++, badge MUTED): each output passes along the first input of a matching kind and everything after it still runs. A *muted wire* is ignored alone. See [Running a graph](running-graphs.md#running-part-of-a-graph).

## N to O

Node
:   One step of a graph, with inputs on the left and outputs on the right. Not to be confused with a Navisworks [item](#i-to-k). See [Concepts](concepts.md#graph-script-node-wire) and the [node library](nodes/index.md).

Node group
:   A few nodes packed into one reusable node with its own inputs and outputs, stored in the `.dyc` file. See [Node groups](node-groups.md) and [Make and reuse a node group](howto/reusable-node-group.md).

Node pack
:   An extra library of nodes, loaded from a `Packages` folder. A pack is code, so install packs only from authors you trust. See [Writing your own nodes](extending.md) and [Privacy and safety](privacy-and-safety.md#what-a-graph-can-do).

Note
:   A yellow sticky note on the canvas for explaining a graph. See [The editor](canvas-and-nodes.md#notes-and-frames).

Output
:   A socket on the right of a node that carries a result. One output can feed many inputs. See [Inputs, outputs and kinds](ports-and-kinds.md#outputs).

## P to R

Pass-through
:   Nodes that change the model give back the items they were given on an output with the same name, so you can chain them. See [Inputs, outputs and kinds](ports-and-kinds.md#outputs).

Personal use and Professional copy
:   The two editions of CamelGraph. The copy from GitHub or bimcamel.com is free for personal use. The professional copy, for work, is coming soon to the Autodesk App Store. See [Licence](licence.md#which-copy-do-i-need).

Pill
:   See [Multi-input](#l-to-m).

Player
:   See [Script Player](#s-to-t).

Problems list
:   The list of every node that failed or warned in the last run, errors first (++ctrl+shift+e++). See [Read errors and warnings](howto/read-errors-and-warnings.md).

Property
:   A named value on a model item, inside a [category](#c-to-d). Nodes ask for it as `propertyName`.

Quick search
:   The small search box that opens when you press ++space++ over the canvas. See [Node library and search](library-and-search.md#quick-search-with-space).

Reading node and writing node
:   A reading node only looks at the model (`Search.*`, `Properties.*`). A writing node changes the model, a file, a program or the web. See [Concepts](concepts.md#what-a-node-changes).

Replication
:   When a list goes into an input that wants one value, the node runs once for each item and gives a list of results. See [Concepts](concepts.md#lists-replication-and-lacing).

Reroute
:   A small waypoint on a wire that bends it. See [The editor](canvas-and-nodes.md#wires).

Run
:   Execute the nodes that changed since the last run (++f5++). See [Running a graph](running-graphs.md).

Run up to a node
:   Run only the selected nodes and what they depend on, then stop (++shift+f5++). See [Running a graph](running-graphs.md#running-part-of-a-graph).

## S to T

Saved viewpoint
:   A named view in the Navisworks Saved Viewpoints window: a camera, and optionally the overrides. `Viewpoint.SaveCurrent` and `Viewpoint.SaveWithOverrides` make them. See [Save one viewpoint for every item](howto/viewpoint-per-item.md).

Script
:   A graph kept in a folder the Script Player knows. See [Saving and opening](saving-opening.md#script-description-and-the-scripts-folder).

Script Player
:   A pane that runs a saved graph from a simple form built from its input nodes, without opening the editor. See [The Script Player](player.md).

Search set
:   A live Navisworks set that stores a rule and re-evaluates as the model changes. `SelectionSet.CreateFromSearch` and `SelectionSets.BulkByPropertyValues` make them. See [Save selection sets](howto/save-selection-sets.md).

Selection set
:   A saved Navisworks set of the items it was given, listed in the Sets window. `SelectionSet.Create` makes one. See [Save selection sets](howto/save-selection-sets.md).

Socket (port)
:   The connection point on a node: inputs on the left, outputs on the right. Its colour is its kind and its shape says one value or a list. See [Inputs, outputs and kinds](ports-and-kinds.md).

Start screen
:   The screen on an empty canvas with cards for a new script, recent scripts and examples, the version and update notice. See [The editor](canvas-and-nodes.md#the-start-screen).

State
:   What a node shows after a run: idle, executed, warning or error, plus frozen and muted. See [Running a graph](running-graphs.md#node-states).

Table
:   Data in columns with names and rows of cells, made by `Properties.ToTable`, `Table.FromRows`, `Table.FromExcelFile` and others, and changed by the `Table.*` nodes. See [Take quantities out to Excel](howto/quantity-takeoff-to-excel.md).

Type letters
:   The optional letter inside each socket that names its kind (**Settings ▸ Appearance ▸ Type letters in sockets**). See [Settings](settings.md#appearance).

## U to Z

Warning
:   The amber node state: the node ran with a recoverable problem, or an input node before it failed. See [Running a graph](running-graphs.md#node-states).

Watch
:   A display node that shows the value wired into it: `Watch`, `Watch List`, `Watch Table` and `Watch Image`. The Script Player shows their values after a run. See [Inputs, outputs and kinds](ports-and-kinds.md#the-watch-nodes).

Wire
:   The line from an output to an input that carries a value. Data flows from left to right. See [Concepts](concepts.md#graph-script-node-wire).

Zero-touch
:   A node written as a public static C# method with a few attributes. The built-in libraries and node packs are made this way. Nodes with controls of their own, such as the sliders and the Watch nodes, are written differently. See [Writing your own nodes](extending.md).
