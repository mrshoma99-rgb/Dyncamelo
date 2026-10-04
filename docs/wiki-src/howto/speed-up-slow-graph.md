---
title: Speed up a slow graph
order: 160
summary: Find the slowest node, stop work you do not need, and keep long runs under control on a large model.
---

# Speed up a slow graph

Goal: make a graph that takes too long on a large model quicker to work with.

## Before you start

* Runs happen on the Navisworks main thread. Navisworks is busy until a run ends, and a progress overlay shows which node is working (for example `12 / 40 - name`).
* A run executes only what changed. If a graph is slow every time, either many nodes changed or a node does a lot of work.
* Save the model before long runs.

## Steps

1. **Read the status bar.** It shows the time of each run. When a run takes a second or more, it also names the slowest node, for example "slowest: Viewpoint.Save 71,200 ms (17×)". Start with that node.
2. **Stop a run you do not need.** Press ++esc++. The run halts before the next node, between the items of a node that is working through a list, or between the passes of a loop. A single Navisworks call already under way cannot be interrupted. The next **Run** carries on where it stopped, and what finished nodes already changed is kept.
3. **Switch Auto off.** In the run bar, set **Manual** and press ++f5++ when you are ready. In Auto mode a run starts after every edit.
4. **Run only part of the graph.** Select a node and press ++shift+f5++ (**Run Up to Selected Node**). It runs that node and what it depends on, then stops. Everything after it waits for the next ordinary Run.
5. **Freeze a slow branch.** Select its first node and press ++shift+m++. A frozen node and everything after it keep their last results and are skipped. Press ++shift+m++ again to unfreeze.
6. **Pass smaller lists.** Some nodes touch every item of the model, and their descriptions say so: `Model.Statistics` and `Search.ByGuid` (they walk every item once), `Appearance.Focus` (allow a moment on large models), and `Distance.BetweenItems` and `Proximity.NearestDistance` with `method` set to `mesh`. Search first, then pass the nodes only the items they need. Prefer `bbox` where it is accurate enough.
7. **Count the loop.** A loop runs its body once for each item, so a slow node inside `Loop.Item` ... `Loop.Collect` is slow once for each item.
8. **Simplify the canvas for very large graphs.** With many nodes (not many model items), turn on **Settings ▸ Canvas ▸ Straight wires**.

![The progress overlay during a run: the node that is working, its position in the run and the hint that Esc cancels.](../../images/wiki-run-progress.png)

![A frozen branch, ghosted with the FROZEN badge, next to a muted node and a live chain.](../../images/wiki-mute-freeze.png)

## What you get

A graph that runs only what you need, when you ask for it. For the canvas itself, **View ▸ Performance HUD** (++ctrl+shift+f12++) shows frames per second, frame time, visuals and node counts. It measures the canvas, not the model, and has a **Copy report** button. It has not yet been verified inside Navisworks, so if nothing happens, use the View menu or the command palette (++ctrl+shift+p++).

!!! tip "Slow IFC export or many viewpoints"
    `Export.ToIfc` reads the geometry of every item, and a loop that saves a viewpoint for each item does real work in Navisworks on every pass. Both are expected to take time. Run them by hand with Auto off, and stop with ++esc++ if you started by mistake.

## If it does not work

* Nothing seems to run: see [Nothing happens when I press Run](../troubleshooting.md#nothing-happens-when-i-press-run).
* The full list of tips is in [A run is slow on a large model](../troubleshooting.md#a-run-is-slow-on-a-large-model). If you measure a slow case, add the model size, the node and the time to an issue.

## Next

* [Running a graph](../running-graphs.md#speed-on-large-models).
* [Read errors and warnings](read-errors-and-warnings.md).
* [Save one viewpoint for every item](viewpoint-per-item.md).
