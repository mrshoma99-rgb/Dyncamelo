---
title: Make and reuse a node group
order: 140
summary: Pack a few nodes into one node with its own inputs and outputs, then use it again and again.
---

# Make and reuse a node group

Goal: turn "find the items on a level and colour them" into one node you can drop onto the canvas as often as you like.

## Before you start

* Open the CamelGraph editor. A model is only needed to run the example.
* A node group is not a **frame**. A frame (++ctrl+g++) is a coloured rectangle behind some nodes. A group is a real node with sockets of its own.

## Steps

1. Build the example. Add a `String` node (*Input*) for the level name. Add `Search.ByProperty` (*Navisworks ▸ Search*) with `categoryName` = `Element`, `propertyName` = `Level`, `mode` = `equals`, and wire the `String` into its `value`. Add `Appearance.OverrideColor` (*Navisworks ▸ Appearance*), wire the search `items` into it, and wire a `Color Picker` (*Input*) into its `color`. Add a `Watch List` (*Display*) on the `items` output.
2. Select the search and the colour override. Leave the `String`, the `Color Picker` and the `Watch List` outside. (If you select them too, CamelGraph keeps input and Watch nodes outside the group on its own, because the Script Player only sees the top level, and tells you which ones it kept out.)
3. Choose **Node Groups ▸ Make Node Group** or press ++ctrl+alt+g++. An instance replaces your selection and keeps the same wires. The wires that crossed the edge of the selection became its sockets: two inputs and one output. The graph computes what it did before.
4. Select the instance and press ++tab++. You see the group's nodes between a **Group Input** and a **Group Output** node, and a bar above the canvas such as `My script ▸ Node Group`.
5. Right-click a socket on Group Input or Group Output and choose **Rename Socket…**. Use `Level`, `Colour` and `Painted items`. Choose **Socket Type** to set `Text` or `Colour`; the socket then takes that colour and an editor on the instance.
6. Choose **Node Groups ▸ Rename Node Group…** and call it `Colour a level`.
7. Press ++shift+tab++, or click **Close group** in the bar, to go back.
8. Use it again. Press ++space++ over the canvas and type `Colour a level`, or open the **Node Groups** folder in the library. Wire a different level name and colour into the new instance.

![A node group instance with its inputs and outputs, next to the rest of the graph.](../../images/wiki-node-group-instance.png)

![The same group opened: Group Input, the nodes, Group Output and the breadcrumb bar.](../../images/wiki-node-group-open.png)

## What you get

A reusable node. Editing the group changes **every instance**. To give one instance its own copy, use **Node Groups ▸ Make Node Group Single User**. To put the nodes back in place of an instance, press ++ctrl+alt+u++ (**Ungroup Node Group**). A group stays in the file after its last instance is gone, until you choose **Delete Unused Node Groups**.

To add a socket quickly while a group is open, drag a wire onto the Group Output node itself, or from an unwired input onto the Group Input node. The new socket is named and typed after the socket you dragged from.

!!! note "Good to know"
    * Everything inside a group runs each time the instance runs.
    * Running (++f5++) still runs the whole graph while a group is open. ++esc++ stops it.
    * Each level keeps its own undo history, so ++ctrl+z++ inside a group never reaches outside it.
    * Errors inside a group are reported on the instance, for example "Inside 'Colour a level', Appearance.OverrideColor: …".
    * A group can hold other groups, but never itself.

!!! warning "Sharing and the Player"
    Older versions of CamelGraph refuse a file that contains node groups, so tell the other person to update ([Saving and opening](../saving-opening.md#sharing-a-script)). The Script Player does not offer inputs that sit inside a group, and does not show a Watch inside one. Make Node Group keeps those nodes at the top level for you; if you move one in by hand, move it out again and wire it to the group.

## If it does not work

The status bar says why a group could not be made. Typical reasons: nothing is selected, a node that sits between two selected nodes is not selected itself, a loop is split (`Loop.Item` and `Loop.Collect` must go into the same group), or the selection holds a Group Input or Group Output node. Select the missing node and try again.

## Next

* [Node groups](../node-groups.md) lists every command and shortcut.
* [Give colleagues a form with the Script Player](form-for-colleagues.md).
* [Keep a graph going when a node fails](keep-going-after-failure.md).
