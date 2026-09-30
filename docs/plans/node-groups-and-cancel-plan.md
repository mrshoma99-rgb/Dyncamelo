# Node groups and cancellable runs — plan

Two features, built in this order: **A. cancel a run** (small, self-contained), then **B. node groups** (Blender-style,
three steps). Every user-facing action goes through `CommandCatalog` so it appears in the menus, the palette, help,
Settings ▸ Shortcuts and the generated guide.

## A. Cancel a run

What exists: the engine already checks `EvaluationContext.CancellationToken` between nodes and returns
`RunResult.Cancelled`, but the editor never supplies a token and runs block the UI thread (Navisworks API calls must run on
the host's main thread), so nothing can set one.

Design:

* **Engine hooks (Core).** `EvaluationContext` gains a settable token, a `Heartbeat` callback and a `ProgressCallback`.
  The engine calls the heartbeat before every node, every replicated call and every loop pass, and reports
  "node *k* of *n*" before each node. It checks the token after each heartbeat, so a 17×-replicated node stops between
  calls, not only between nodes. A node cut short (or a loop cut mid-way) **stays dirty**, keeps its previous outputs and
  is not published as clean, so the next run resumes exactly there.
* **Esc while the thread is blocked (UI).** The heartbeat polls the keyboard state directly (`GetAsyncKeyState(VK_ESCAPE)`,
  only while a window of this process is in the foreground), so no messages need pumping and the user cannot edit the graph
  mid-run. The same heartbeat repaints the busy overlay at Render priority (never Input), at most every ~100 ms, with
  "Running 12 / 40 — *node* (Esc to cancel)".
* **Limits, stated honestly.** Cancellation takes effect between node calls. A single long Navisworks call cannot be
  interrupted, and what a node already changed in Navisworks is not rolled back (the status line says so).
* **Surface.** `graph.cancel` command (Esc while running), a hint on the overlay, status text
  "Run cancelled after 12 of 40 nodes — N still to run", a line in the guide.

## B. Node groups

Vocabulary: the existing **Group Selection** is a visual frame (`GroupModel`, `Ctrl+G`). A **node group** is a reusable
subgraph. In code: `NodeGroup` (definition), `GroupInstanceNode`, `GroupInputNode`, `GroupOutputNode`, `NodeGroupLibrary`.

### Model (G1, Core)

* A **definition** is `{Id, Name, Inputs[], Outputs[], Graph}`. The interface (named sockets with stable ids and a kind
  hint) lives on the definition; the inner `GroupInputNode` / `GroupOutputNode` mirror it, and every instance mirrors it.
  Changing the interface syncs all of them. Sockets are `object`-typed, so lists pass whole (no replication).
* All definitions of a file live in one **library** shared by the root graph and every inner graph (nested groups are
  allowed; a group can never contain itself, directly or through another group).
* **Evaluation.** An instance runs the inner graph with a fresh engine: inner nodes are all marked dirty quietly, the
  input node is fed the instance's inputs, the output node captures what reaches it. Cancel, progress and scope
  ("Outer ▸ Inner") flow through the same `EvaluationContext`. Editing a definition dirties every instance.
* **File.** `.dyc` gains an additive `NodeGroups` array (each with its interface and a nested graph body) and instances are
  nodes of type `NodeGroup` with `Data.GroupId`. `MinReaderVersion` becomes 2 **only** for files that contain groups, so
  older readers refuse them instead of silently dropping the definitions; files without groups are unchanged.
* **Looks.** An instance is an ordinary node card: title = group name, sockets from the interface.

### Editing operations (G2, Core, all undoable as one step)

* **Make group from selection:** wires crossing into the selection become input sockets (one per distinct outside source),
  wires leaving become output sockets; the selection moves into a new definition; an instance replaces it, wired to the old
  neighbours at the same place. Pinned values on boundary inputs stay inside.
* **Ungroup:** inlines the instance's nodes back into the outer graph, re-wired through the interface.
* **Interface edits:** add, remove, rename, reorder sockets (instances keep their wires by socket id).
* **Duplicate / copy-paste** an instance shares the definition; **Make single user** copies the definition.
* Deleting the last instance does not delete the definition (it stays in the library, like Blender's orphan data).

### Editor (G3, UI)

* **Enter / exit:** `Tab` enters the selected instance and exits from inside; also a breadcrumb bar
  (*Graph ▸ Group ▸ Inner*) and double-click on the header. The editor shows the inner graph with its own undo history;
  the outer history is kept while inside.
* **While inside**, runs always run the **root** graph with the instances of the edited group dirty, so edits show their
  effect immediately; inner previews show the values of the last instance that ran.
* **Interface:** the Group Input / Output nodes carry "+" / "−" and a rename action; dropping a wire on an empty interface
  socket creates the socket.
* **Library:** a *Node Groups* category lists the file's definitions (favourites and search work), so instances are added
  like any node.
* **Commands:** `group.make`, `group.ungroup`, `group.edit` (Tab), `group.exit`, `group.rename`, `group.singleuser`,
  `group.addinput`, `group.addoutput`.

## Order of work and tests

1. A: engine hooks + tests (Core, Linux) → UI wiring + tests (CI) → release.
2. G1: model, evaluation, serialization + tests (Core/Integration, Linux).
3. G2: operations + undo + tests (Linux).
4. G3: editor UI + tests (CI), docs, guide, release.
