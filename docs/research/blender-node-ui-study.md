# Blender's node system: a UI/UX study for Dyncamelo

*Purpose: understand why Blender's node editors feel fluid and uniform, decide which of those ideas transfer to a Navisworks graph editor, and turn them into a phased plan. Companion to [plugin-ecosystem-analysis.md](plugin-ecosystem-analysis.md).*

**How to read the confidence markers.** Claims tagged **[verified]** come from Blender's manual or developer blog pages I read while writing this (linked at the end). Claims tagged **[recalled]** are from general knowledge of Blender and were *not* re-checked against a source — treat them as hypotheses to confirm in a running Blender before we design against them. Claims tagged **[proposal]** are mine, not Blender's.

---

## 1. The one-paragraph answer

Blender's nodes feel good because of **one visual grammar applied without exceptions**. Every node is the same object: a header, then outputs, then settings, then inputs. Every value is the same kind of widget: a full-width number field you can drag, click-to-type, or snap. Every socket says two things at a glance: its *type* (colour) and its *structure* (shape). Every unconnected input carries its own editor, so a node is usable *without wiring anything*. Then a large set of small link-manipulation gestures (insert-on-link, swap, cut, mute, reroute) make editing a graph feel like handling rope, not filling in a form. None of these ideas is exotic. The feel comes from doing all of them consistently.

Dyncamelo today has the *components* of this (category-coloured headers, dropdowns for choice inputs, sliders, a boolean switch, value previews) but applies them only in **special node types** (Number, Slider, Boolean…), not to *every* node's inputs. That is the single biggest gap, and it is why our nodes read as "stiff": a typical Navisworks node is a header plus two columns of labelled dots, and nothing on it is directly editable.

---

## 2. Anatomy of a node

**[verified]** from the Node Parts manual page:

| Part | Behaviour |
|---|---|
| **Header** | Title = node name, overridable with a *Label*. A **collapse arrow sits left of the title** (also `H`). A **preview toggle sits top-right** on nodes that support one. |
| **Outputs** | Listed first, right side. |
| **Properties** | The node's *settings* (dropdowns, checkboxes) sit **between outputs and inputs**. |
| **Inputs** | Listed last, left side. **When disconnected, an input shows an editable default value** in place. |
| **Special sockets** | Multi-input sockets get an **ellipsis shape** instead of a circle. |
| **Panels** | Collapsible groups of sockets, added in 4.0; **always appear after the sockets** in a node. |

Points worth noticing:

1. **Ordering is fixed and meaningful**: results → configuration → data-in. The eye learns where to look on *every* node.
2. **"Disconnected input = its own editor"** is the core of the pattern. The socket dot is the *connection point*; the widget beside it is the *value*. Connecting a wire hides the widget. This is why Blender nodes rarely need a separate "value" node for a constant.
3. **Hide unused sockets** (`Ctrl+H`) **[verified]** and **collapse the whole node** (`H`) let one node be dense or minimal without changing what it does.
4. Nodes are **resizable by dragging left/right borders** **[verified]** — width is a user decision, height follows content.

---

## 3. The type language: colour × shape

### 3.1 Colour = data type

**[verified]** socket colours (the manual's list):

| Type | Colour |
|---|---|
| Shader | bright green |
| Geometry | turquoise |
| Boolean | pink |
| Colour | yellow |
| Float | grey |
| Integer | lime green |
| String | light blue |
| Vector | dark blue |
| Data-blocks: Collection / Object / Material / Texture / Image | white / orange / salmon / pink / apricot |

Consequence **[recalled, but consistent with the manual's "Wire Colors" overlay]**: wires can be tinted by the type they carry, so you can *read a graph's data flow from across the room* without hovering anything. The manual confirms a **Wire Colors overlay** ("color node links based on their connected sockets") **[verified]**.

There is also **implicit conversion** between compatible types (colour↔vector, colour↔float, colour/float/vector→shader) with **explicit conversion nodes** where it is lossy **[verified]**. The UX point: *connecting is permitted whenever it is meaningful, and the system does the boring cast*.

### 3.2 Shape = data structure

Blender's Geometry Nodes needed a second channel, because "float" says nothing about *one value vs. one per point*. **[verified]** (developer blog, "New Socket Shapes"):

- **Old system (≤4.x):** circle = single value, diamond = field, diamond-with-dot = "field, currently a single value."
- **Problem they hit:** the dotted diamond was "especially fuzzy"; shapes *changed with connections*, so users could not tell whether a shape meant *what the node expects* or *what is currently flowing*; and lists / volume grids would need so many shapes "they would stop being useful."
- **New rule (5.0):** *shapes describe what a socket expects or produces, independent of usage.* Vertical bar = single value, diamond = field, circle = flexible/dynamic; shapes no longer change on connection (except group inputs/outputs).
- **A second, subtler channel — dashed links:** a dashed wire means "a field passes here and needs a context to evaluate." It adds information *on top of* the shape system rather than being redundant with it.
- **What they gave up, explicitly:** right-to-left propagation display, per-field default indicators. They accepted the loss for scalability and said they would wait to see "what information is really missing."

The design lesson is more valuable than the shapes themselves: **use colour for "what kind", shape for "how much/structure", line style for "how it behaves", and put everything else in the tooltip** — the May 2024 workshop notes **[verified]** say exactly this ("tooltips … might solve this well enough") and propose *greying out sockets you cannot connect to while dragging a link*.

---

## 4. Widget language: the number field

**[verified]** from the Input Fields manual page and search results:

- A **number field** shows `<` `>` arrows on hover for unit steps; a **slider** is a number field with a **coloured fill bar** showing position in a range.
- **Drag** with LMB to change value. **`Ctrl` snaps** to steps while dragging; **`Shift` gives precision**.
- **Click without dragging = type a value.** `Enter`/click-away applies; `Esc`/RMB cancels. **`Tab` / `Shift+Tab` move to the next/previous field.**
- **Ctrl+wheel** over a field steps the value.
- **Multi-field drag:** press on one field and drag *vertically* across several to edit them together.
- **Soft vs. hard limits:** dragging clamps to the *soft* range; typing may exceed it up to the *hard* range.

Why this matters for us: a single widget serves **coarse adjustment (drag), fine adjustment (Shift), exact entry (click-type), and keyboard flow (Tab)**. Our current slider node covers coarse/exact in a dedicated node, but nothing inside an ordinary node does.

---

## 5. Interaction model: editing a graph like rope

**[verified]** (Editing Nodes manual + Node Wrangler docs + link-swapping design thread):

| Gesture | Effect |
|---|---|
| Drag output → input | Create link |
| Drag link **off** an input and release | Disconnect |
| **Drag a wire and drop on empty canvas** | Opens a **search menu of compatible nodes**; the chosen node is inserted *already connected* |
| **Drag a node onto an existing link** | **Inserts the node into the link**, using the first socket matching the link type; surrounding nodes **auto-offset** to make room (`T` toggles offset direction, `Alt` toggles the whole behaviour) |
| `Ctrl`-drag from an output | *Move* existing outgoing links instead of adding a new one |
| `Alt` while moving a link | **Swap** links between occupied sockets |
| `Ctrl+RMB` drag across links | **Cut** links |
| `Ctrl+Alt+RMB` drag | **Mute** links (a muted link "acts as though it is no longer there") |
| `M` | Mute node (its links turn red) |
| `X` / `Delete` vs. `Ctrl+X` | Delete vs. **delete and reconnect** input→output (heals the chain) |
| `F` / `Shift+F` | Auto-make links between selected nodes / replace existing |
| `Shift+D` / `Alt+D` | Duplicate / duplicate linked |
| `Shift+RMB` drag across a wire | **Insert a reroute** (a pure routing dot) |
| Frames, reroutes | Layout-only nodes for organisation |
| `H` / `Ctrl+H` / `Shift+H` | Collapse node / hide unused sockets / toggle preview |
| Middle-drag, wheel, `Home`, `Numpad .` | Pan, zoom, frame all, frame selection |

Design points worth stealing:

1. **Explicit beats clever.** The link-swap thread **[verified]** is instructive: Blender *removed* automatic swapping because "the precise logic … is intransparent and it's not obvious when a replaced link will automatically reconnect," and replaced it with an explicit `Alt` modifier. Skeptics noted auto-swap gave a "temporary location" benefit — a real trade-off, not a free win.
2. **Every destructive or structural action has a healing variant** (delete-and-reconnect, insert-into-link, swap). The graph rarely needs to be re-wired by hand after an edit.
3. **The wire is a first-class object**: cut, mute, reroute, insert-on. In most node editors the wire is passive.
4. **Node Wrangler's "Lazy Connect"** (drag between two nodes without touching sockets) **[verified]** exists as an add-on *because* precision-clicking small sockets is the tool's biggest friction. Large hit targets are a real usability problem, not a cosmetic one.

---

## 6. Managing scale

Blender's answer to "this graph got huge", **[verified]** unless noted:

- **Collapse** a node to its header; **hide unused sockets**; **panels** that fold groups of sockets.
- **Frames** (labelled, coloured boxes) group nodes visually; **reroutes** clean up long wires.
- **Node groups** turn a subgraph into one node with a *designed interface*; the 2024 workshop discussed **local groups** that "don't leak out" to the add menu, plus **multi-line comments and custom zone colours** for readability.
- **Link readability was tuned in 4.0** "at different zooms and monitor DPI" — wire thickness/behaviour scale with zoom rather than staying constant.
- **Direct renaming** in the node (double-click / `Ctrl`+click) was proposed instead of routing through a sidebar.

Dyncamelo already has groups (`Ctrl+G`), notes, and Arrange (`Ctrl+L`). Missing: collapse, hide-unused-sockets, reroutes, panels.

---

## 7. Design principles, distilled

1. **Uniformity over cleverness.** One node anatomy, one widget family, one socket vocabulary. Users learn the *system*, then every new node is free.
2. **A node is usable unwired.** Defaults are visible and editable in place.
3. **Two channels for type: colour = kind, shape = structure.** Extra nuance goes in line style and tooltips, not more shapes.
4. **Shapes describe expectations, not current state.** (Blender learned this the hard way.)
5. **Fast paths for the common case, precise paths for the rare one** (drag / Shift / type / Tab).
6. **Structure-changing edits should heal the graph** (insert-on-link, delete-reconnect, swap).
7. **Density is the user's choice** (collapse, hide, panels) — never a different node.
8. **Explicit over automatic** when the automatic rule cannot be explained in one sentence.
9. **Prevent errors while dragging**: grey out sockets that cannot accept the link being dragged.

---

## 8. Where Blender's model does *not* fit Dyncamelo

Copying blindly would be a mistake. Differences that matter:

| Blender | Dyncamelo | Consequence |
|---|---|---|
| Types are few and numeric-heavy (float, vector, colour…) | Types are mostly **Navisworks objects** (ModelItem, ClashResult, SavedViewpoint, Document…) and **lists** of them | Colour-by-type needs a *category* scheme for object types, not one hue per class |
| "Field" = per-element evaluation | **Lists + lacing** are our structure axis | Our second channel (shape) should encode **single vs. list vs. nested list**, not field |
| Instant, side-effect-free evaluation | **Manual run, side effects, warning/error states**, frozen nodes, previews | Node border/badge state already carries this; must survive any restyle |
| Every node has a small, numeric-friendly surface | Many nodes take **object inputs that cannot have a typed-in default** | "Inline editor when unwired" applies only to *value-like* inputs (number, bool, text, choice, colour); object inputs stay plain sockets |
| GPU-drawn custom widgets | **WPF templates on Nodify** | Everything must be expressible as templates/styles; drag-to-scrub needs custom input handling |
| Users are artists who expect to scrub values | Users are **BIM coordinators** who expect exact values and reproducibility | Typed entry and clear units matter more than scrubbing; scrubbing is a bonus |

---

## 9. Gap analysis: Dyncamelo today vs. this grammar

From reading `DyncameloDark.xaml`, `NodeViewModel`, `ConnectorViewModel`:

| Blender idea | Dyncamelo now |
|---|---|
| Uniform header/outputs/settings/inputs order | Nodify default: inputs left, outputs right, side by side. **[gap]** |
| Category-coloured header | ✅ `HeaderBrush` by root category |
| Collapse arrow in header | ❌ |
| Unwired input shows an editable default | ⚠️ Only **choice** inputs (dropdown) and a few special nodes; ordinary value inputs are edited elsewhere. `ConnectorViewModel` already has `UserValue` plumbing to build on |
| Socket colour = type | ❌ all ports look alike (type only in tooltip) |
| Socket shape = structure | ❌ (we have `DeclaredType`, so rank single/list is derivable) |
| Wire colour follows type | ❌ single wire colour |
| Number field widget (drag/Shift/type/Tab) | ⚠️ separate Slider nodes only |
| Boolean as a switch | ✅ (`NodeBody.BooleanToggle`) |
| Dropdown inside node | ✅ for choice ports |
| Rounded, softer surface | ⚠️ `CornerRadius` exists on the state border only; body/header are Nodify defaults |
| Insert node on link / delete-reconnect / swap / cut / mute | ❌ |
| Drop-wire-on-canvas opens filtered search | ❌ (we have space-bar search, but not link-aware) |
| Reroutes, hide unused sockets, panels | ❌ |
| Grey out invalid sockets while dragging | ❌ |
| Resizable node width | ⚠️ Watch nodes only |

---

## 10. Proposed design system  **[proposal]**

### 10.1 Socket colour by *category of type*

Blender uses one hue per class; we have hundreds of Navisworks classes, so group them:

| Family | Types | Suggested hue |
|---|---|---|
| Number | double, int | grey / lime (int) |
| Boolean | bool | pink |
| Text | string, paths | light blue |
| Colour | Color | yellow |
| Model items | ModelItem | orange |
| Clash | ClashTest / ClashResult / group | red-orange |
| Viewpoint / saved items | SavedViewpoint, SelectionSet, Folder | teal |
| Geometry | Point, Vector, BoundingBox | dark blue |
| Document / generic object | Document, object | neutral |

Palette to be validated for **colour-blind safety** (the socket-shape article did not address accessibility, so we should not assume Blender solved it — pair colour with shape below).

### 10.2 Socket shape by *structure* (our "field" axis is lists)

- **Circle** = single value
- **Rounded square / bar** = list of values
- **Stacked/double outline** = nested list (rank ≥ 2)
- **Diamond** = `object` (accepts anything) — the "flexible" socket

Rule taken straight from Blender's lesson: **shape reflects what the port declares, not what is connected.**

### 10.3 Wires
Tint by output type; **dashed** when the wire carries a list that will *replicate* the receiving node (our analogue of "field needs a context") — this makes lacing visible for the first time.

### 10.4 Node body
Fixed order: header (collapse arrow · title · state badge · preview eye) → outputs → settings (choice/bool ports as in-node dropdowns/switches) → inputs with inline editors when unwired. Rounded corners, subtle drop shadow, header tinted by category, state (idle/warn/error) as border + badge exactly as now.

### 10.5 Input widgets (unwired only)
Number → field with `<`/`>` on hover, drag to scrub, `Shift` precise, click to type, `Tab` to next. Bool → switch. Choice → dropdown. Text → single-line box. Colour → swatch opening the existing colour dialog. Anything else → bare socket.

### 10.6 Graph-editing gestures (in value order)
1. Drop a wire on empty canvas → link-aware search that inserts the node connected.
2. Delete-and-reconnect (`Ctrl+Delete`).
3. Drag node onto wire → insert (with auto-offset).
4. Collapse node / hide unused sockets.
5. Reroute dots; cut links with `Ctrl`-drag.
6. Grey out incompatible sockets during a drag.

---

## 11. Phasing and risks  **[proposal]**

| Phase | Scope | Risk |
|---|---|---|
| **A — Look** | Rounded body/header, spacing, shadows, uniform anatomy, socket colour by type family, wire tint | Low logic risk; **cannot be visually verified from a Linux build** — ship small, review on your machine |
| **B — Inline editors** | Unwired value inputs get number/bool/text/colour editors; collapse arrow; hide-unused | Medium: new templates per type; must not break `.dyc` (values already stored as `UserValue`) |
| **C — Structure channel** | Socket shapes for single/list/nested, dashed replicating wires | Medium: touches port templates and connection style; Nodify customisation depth to be confirmed |
| **D — Gestures** | Insert-on-link, delete-reconnect, wire-drop search, reroutes, cut/mute | Highest: needs hit-testing and undo-safe graph edits; each is separable |

**Cross-cutting constraints:** keep every existing state cue (frozen ghosting, warning/error border, preview bubble); keep `.dyc` format unchanged; performance with 200+ nodes (templates with many bindings per node are the usual WPF cost).

## 12. Open questions for you

1. Are typed-in defaults on **unwired number/bool/text inputs** the most valuable single change? (My read: yes — it removes most "value nodes" from graphs.)
2. Colour-blind users on your team? That decides whether shape-as-second-channel is mandatory or optional.
3. Do you want **scrub-to-change** on numbers, or exact typed entry only? (BIM users often prefer the latter.)
4. Density preference: Blender-compact, or roomy?
5. Which gesture would you use daily — insert-on-wire, delete-reconnect, wire-drop search?

---

## Sources

- Blender Manual — [Node Parts (3.6)](https://docs.blender.org/manual/en/3.6/interface/controls/nodes/parts.html), [Editing Nodes (3.6)](https://docs.blender.org/manual/en/3.6/interface/controls/nodes/editing.html), [Nodes introduction (3.6)](https://docs.blender.org/manual/en/3.6/interface/controls/nodes/introduction.html), [Input Fields](https://docs.blender.org/manual/en/latest/interface/controls/buttons/fields.html)
- Blender Developers Blog — [New Socket Shapes (2025)](https://code.blender.org/2025/08/new-socket-shapes/), [Geometry Nodes Workshop, May 2024](https://code.blender.org/2024/05/geometry-nodes-workshop-may-2024/)
- Blender Developer Docs — [4.0 Node Editor release notes](https://wiki.blender.org/wiki/Reference/Release_Notes/4.0/Node_Editor)
- Blender DevTalk — [Feedback wanted on node link swapping](https://devtalk.blender.org/t/feedback-wanted-on-node-link-swapping/26302)
- Blender Manual — [Node Wrangler add-on](https://docs.blender.org/manual/en/latest/addons/node/node_wrangler.html)
- Nodify source (v7.3.0), read while investigating the editor: [github.com/miroiu/nodify](https://github.com/miroiu/nodify)

*Not verified against a source: Blender's node header colours per category, the exact corner rounding, and whether the wire-colour overlay is on by default. Confirm in a running Blender before matching them.*
