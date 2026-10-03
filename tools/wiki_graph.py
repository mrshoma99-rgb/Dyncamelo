#!/usr/bin/env python3
"""Build Dyncamelo `.dyc` graphs for the wiki from small JSON specs.

The wiki shows pictures of real graphs and offers them for download. Writing a
`.dyc` by hand is slow and easy to get wrong (a misspelt port silently drops a
wire when the file opens), so each how-to graph is a short *spec* and this tool
turns it into the file the editor would have saved: real node ids, ports,
defaults and input-node data, a tidy left-to-right layout, deterministic ids.

Usage
-----
    python3 tools/wiki_graph.py                  build every spec in docs/wiki-src/graphs/specs/
    python3 tools/wiki_graph.py first-script     build one graph (spec name, or a path to a spec)
    python3 tools/wiki_graph.py --check          fail when a committed .dyc is not what its spec builds
    python3 tools/wiki_graph.py --node Search.ByProperty
                                                 show a node: its id, ports, editors, defaults and choices

Specs live in `docs/wiki-src/graphs/specs/<name>.json`; the graph is written to
`docs/wiki-src/graphs/<name>.dyc` (the wiki links it as `../graphs/<name>.dyc`).
Rebuilding an unchanged spec gives a byte-identical file, so `--check` can run in CI.

A spec
------
    {
      "name": "First script",
      "description": "One sentence: what the graph does.",
      "run": "Manual",
      "nodes": [
        {"id": "text", "node": "String", "title": "Material text", "value": "Concrete"},
        {"id": "search", "node": "Search.ByProperty",
         "values": {"categoryName": "Element", "propertyName": "Material", "mode": "contains"}},
        {"id": "show", "node": "Watch List", "title": "Matching items"}
      ],
      "wires": ["text.value -> search.value", "search.items -> show.list"],
      "notes": [{"text": "What it does and what to change."}],
      "frames": [{"title": "1 - Find", "nodes": ["text", "search"], "colour": "Blue"}]
    }

* `id` is yours: a short name used by `wires`, `frames` and the generated ids.
* `node` is a name from `docs/dyncamelo-nodes.json` (`Search.ByProperty`, `Table.Sort`, ...) or one of the
  built-in kinds below. `title` renames the node on the canvas.
* `values` types a value into an input that is not wired, exactly as the editor's inline editor would:
  a number (number or integer inputs), `true`/`false` (toggles), text (text and path inputs), one of the listed
  choices (drop-downs) or `"#RRGGBB"` (colour inputs). Inputs that have no inline editor (a plain `any`/object
  input, a list, a table) cannot be typed: wire an input node such as `String` or `Number` into them. The tool
  says so, and refuses unknown nodes, unknown ports, wrong types, values outside a range, wires into the wrong
  kind of port, two wires into a single-wire input and required inputs that are neither wired nor typed.
* `wires` are `"from.port -> to.port"`. The port may be left out when the node has only one (`"text -> search.value"`).
* Built-in kinds and their fields: `String` (`value`), `Number` (`value`), `Integer` (`value`), `Boolean` (`value`),
  `Choice` (`options`: list of texts, `value`: one of them), `Date` (`value`: `2026-10-01`), `Number Slider` and
  `Integer Slider` (`value`, `min`, `max`, `step`), `File Path` and `Directory Path` (`value`), `Color Picker`
  (`value`: `#RRGGBB`), `Watch`, `Watch List`, `Watch Table`, `Watch Image`, `List.Create` (`count`), `Loop.Item`,
  `Loop.Collect`, `Reroute` and `Note` (a sticky note: `text`).
* Optional on any node: `lacing` (`Auto`, `Shortest`, `Longest` or `CrossProduct`), `player` (true shows a node in the
  Script Player), `player_inputs` (names of unwired inputs the Player offers as fields).
* `notes` are sticky notes; `at` is `"top"` (default), `"bottom"` or `[x, y]`. Use one per graph, sparingly.
* `frames` draw a titled rectangle round the listed nodes; `colour` is Blue, Green, Amber, Red, Purple or Gray.

Positions are never written by hand: nodes go into columns left to right (a node sits one column right of the
deepest node that feeds it; input nodes sit next to what they feed) and are stacked so that wires run roughly
level. The order of `nodes` decides the order inside a column when nothing else does.

Adding a graph
--------------
1. Write `docs/wiki-src/graphs/specs/<name>.json`.
2. Run `python3 tools/wiki_graph.py <name>`; fix what it reports.
3. Run `python3 tools/wiki_graph.py --check`, then `dotnet test tests/Dyncamelo.Integration.Tests -c Release`
   (the static validation test also covers every graph in `docs/wiki-src/graphs/`).
4. Commit the spec and the `.dyc` together. Link it from the page as `[Download the graph](../graphs/<name>.dyc)`.

How node ids are found
----------------------
A zero-touch node is saved with a definition id such as
`Dyncamelo.Navisworks.SearchNodes.ByProperty@string,string,object,string,string,Autodesk.Navisworks.Api.Document`
(declaring type, method, then the parameter types the loader writes). When a catalogue entry carries an `id` that
value is used. Otherwise the id is derived here from the C# sources with the loader's own rules
(`AssemblyNodeLoader.GetFunctionSignature`): keyword types stay as written, every other type is written with its
full name, generics without spaces, `Nullable<T>` as `T?`. Everything else a node needs (port names, defaults, choices,
ranges, multi-input, kinds) is read from the same sources and cross-checked against `docs/dyncamelo-nodes.json`.
"""

from __future__ import annotations

import argparse
import difflib
import hashlib
import json
import math
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Iterable, Optional

REPO = Path(__file__).resolve().parent.parent
GRAPH_DIR = REPO / "docs" / "wiki-src" / "graphs"
SPEC_DIR = GRAPH_DIR / "specs"
CATALOGUE = REPO / "docs" / "dyncamelo-nodes.json"
SOURCE_DIRS = [
    ("Dyncamelo.Nodes", REPO / "src" / "Dyncamelo.Nodes"),
    ("Dyncamelo.Navisworks", REPO / "src" / "Dyncamelo.Navisworks"),
]
# Read for the names of the types declared there (parameter types such as NodeRegistry), never for nodes.
TYPE_ONLY_DIRS = [REPO / "src" / "Dyncamelo.Core"]
EXCLUDED_PARTS = {"bin", "obj"}

NODE_ID_NAMESPACE = "dyncamelo-wiki-graph"


class SpecError(Exception):
    """A problem with a spec (or with the node it names); the message says what to change."""


# ===================================================================== C# source reading

class _Unknown:
    """Marks a default value or number the tool cannot evaluate (an expression, an enum member, ...)."""

    def __repr__(self) -> str:  # pragma: no cover - debugging aid
        return "UNKNOWN"


UNKNOWN = _Unknown()


def mask_source(text: str) -> str:
    """Return text of the same length with comments, string and char literals blanked (newlines kept).

    Quotes of a literal stay, its content becomes spaces, so brackets, commas and braces inside strings and comments
    never confuse the structural scans below. Offsets in the masked text equal offsets in the original.
    """
    out = list(text)
    n = len(text)
    i = 0
    while i < n:
        c = text[i]
        nxt = text[i + 1] if i + 1 < n else ""
        if c == "/" and nxt == "/":
            j = text.find("\n", i)
            j = n if j < 0 else j
            for k in range(i, j):
                out[k] = " "
            i = j
        elif c == "/" and nxt == "*":
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            for k in range(i, j):
                if text[k] != "\n":
                    out[k] = " "
            i = j
        elif c in "@$" and (nxt == '"' or (nxt in "@$" and i + 2 < n and text[i + 2] == '"')):
            # verbatim / interpolated string: find the opening quote
            q = i + 1 if nxt == '"' else i + 2
            verbatim = "@" in text[i:q]
            j = q + 1
            while j < n:
                if verbatim and text[j] == '"' and j + 1 < n and text[j + 1] == '"':
                    j += 2
                    continue
                if not verbatim and text[j] == "\\":
                    j += 2
                    continue
                if text[j] == '"':
                    break
                j += 1
            for k in range(q + 1, min(j, n)):
                if text[k] != "\n":
                    out[k] = " "
            i = j + 1
        elif c == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == "\\" else 1
            for k in range(i + 1, min(j, n)):
                if text[k] != "\n":
                    out[k] = " "
            i = j + 1
        elif c == "'":
            j = i + 1
            while j < n and text[j] != "'":
                j += 2 if text[j] == "\\" else 1
            for k in range(i + 1, min(j, n)):
                if text[k] != "\n":
                    out[k] = " "
            i = j + 1
        else:
            i += 1
    return "".join(out)


def parse_cs_string_literals(expr: str) -> Optional[str]:
    """Evaluate `"a" + "b"` style constant strings (regular and verbatim). None when the expression is not one."""
    expr = expr.strip()
    out: list[str] = []
    i = 0
    n = len(expr)
    expect_operand = True
    while i < n:
        c = expr[i]
        if c.isspace():
            i += 1
            continue
        if expect_operand:
            verbatim = False
            if c == "@" and i + 1 < n and expr[i + 1] == '"':
                verbatim = True
                i += 1
                c = '"'
            if c != '"':
                return None
            i += 1
            buf: list[str] = []
            while i < n:
                if verbatim:
                    if expr[i] == '"':
                        if i + 1 < n and expr[i + 1] == '"':
                            buf.append('"')
                            i += 2
                            continue
                        break
                    buf.append(expr[i])
                    i += 1
                else:
                    if expr[i] == "\\":
                        esc = expr[i + 1] if i + 1 < n else ""
                        buf.append({"n": "\n", "t": "\t", "r": "\r", '"': '"', "\\": "\\", "'": "'", "0": "\0"}.get(esc, esc))
                        i += 2
                        continue
                    if expr[i] == '"':
                        break
                    buf.append(expr[i])
                    i += 1
            else:
                return None
            i += 1
            out.append("".join(buf))
            expect_operand = False
        else:
            if c != "+":
                return None
            i += 1
            expect_operand = True
    if expect_operand:
        return None
    return "".join(out)


def parse_cs_number(expr: str) -> Any:
    """A numeric literal (with sign / suffix) or one of the well-known constants; UNKNOWN otherwise."""
    e = expr.strip()
    constants = {
        "double.PositiveInfinity": math.inf,
        "double.NegativeInfinity": -math.inf,
        "double.MaxValue": sys.float_info.max,
        "double.MinValue": -sys.float_info.max,
        "int.MaxValue": 2147483647,
        "int.MinValue": -2147483648,
        "long.MaxValue": 9223372036854775807,
        "long.MinValue": -9223372036854775808,
        "double.NaN": math.nan,
        "Math.PI": math.pi,
    }
    if e in constants:
        return constants[e]
    m = re.fullmatch(r"([+-]?)\s*((?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)([dDfFmMlLuU]{0,2})", e)
    if not m:
        return UNKNOWN
    sign = -1 if m.group(1) == "-" else 1
    body, suffix = m.group(2), m.group(3).lower()
    is_float = "." in body or "e" in body.lower() or suffix in ("d", "f", "m")
    value = float(body) if is_float else int(body)
    return sign * value


def eval_default(text: str) -> Any:
    """Evaluate a C# default-value expression when it is a plain literal; UNKNOWN for anything else."""
    e = text.strip()
    if e in ("null", "default", "default!", "null!"):
        return None
    if e == "true":
        return True
    if e == "false":
        return False
    s = parse_cs_string_literals(e)
    if s is not None:
        return s
    return parse_cs_number(e)


def _split_top_level(masked: str, start: int, end: int, sep: str = ",") -> list[tuple[int, int]]:
    """Spans of masked[start:end] split on `sep` outside (), [], {} and <> (angle brackets only after a name)."""
    spans: list[tuple[int, int]] = []
    depth = 0
    piece = start
    for i in range(start, end):
        c = masked[i]
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        elif c == "<":
            depth += 1
        elif c == ">":
            depth -= 1
        elif c == sep and depth == 0:
            spans.append((piece, i))
            piece = i + 1
    spans.append((piece, end))
    return spans


def _match_forward(masked: str, open_index: int) -> int:
    """Index of the bracket closing the one at open_index (masked text), or -1."""
    pairs = {"(": ")", "[": "]", "{": "}"}
    opener = masked[open_index]
    closer = pairs[opener]
    depth = 0
    for i in range(open_index, len(masked)):
        if masked[i] == opener:
            depth += 1
        elif masked[i] == closer:
            depth -= 1
            if depth == 0:
                return i
    return -1


def _attribute_groups_before(masked: str, text: str, pos: int) -> list[str]:
    """The `[...]` attribute groups (original text, inner part) directly above position pos, in source order."""
    groups: list[str] = []
    i = pos
    while True:
        j = i - 1
        while j >= 0 and masked[j].isspace():
            j -= 1
        if j < 0 or masked[j] != "]":
            break
        depth = 0
        k = j
        while k >= 0:
            if masked[k] == "]":
                depth += 1
            elif masked[k] == "[":
                depth -= 1
                if depth == 0:
                    break
            k -= 1
        if k < 0:
            break
        groups.append(text[k + 1:j])
        i = k
    groups.reverse()
    return groups


@dataclass
class Attribute:
    name: str
    args: list[str]            # original-text arguments, split at top level
    target: str = ""           # "return" for [return: ...]


def parse_attribute_groups(groups: Iterable[str]) -> list[Attribute]:
    """Split attribute groups such as `NodeName("x"), NodeAliases("y")` or `return: NodeName("x")` into attributes."""
    result: list[Attribute] = []
    for group in groups:
        masked = mask_source(group)
        target = ""
        offset = 0
        m = re.match(r"\s*(return|method|param|field|type|assembly)\s*:", masked)
        if m:
            target = m.group(1)
            offset = m.end()
        for a, b in _split_top_level(masked, offset, len(masked)):
            piece = group[a:b].strip()
            pm = mask_source(piece)
            nm = re.match(r"([A-Za-z_][\w.]*)\s*(\()?", pm)
            if not nm:
                continue
            name = nm.group(1).split(".")[-1]
            if name.endswith("Attribute"):
                name = name[: -len("Attribute")]
            args: list[str] = []
            if nm.group(2):
                open_index = nm.end() - 1
                close = _match_forward(pm, open_index)
                inner_start, inner_end = open_index + 1, close if close >= 0 else len(pm)
                if pm[inner_start:inner_end].strip():
                    args = [piece[x:y].strip() for x, y in _split_top_level(pm, inner_start, inner_end)]
            result.append(Attribute(name, args, target))
    return result


def _attr(attrs: list[Attribute], name: str, target: str = "") -> Optional[Attribute]:
    for a in attrs:
        if a.name == name and a.target == target:
            return a
    return None


def _string_args(attr: Attribute) -> list[str]:
    values: list[str] = []
    for arg in attr.args:
        if re.match(r"^[A-Za-z_]\w*\s*=(?!=)", arg):
            continue  # named argument
        s = parse_cs_string_literals(arg)
        if s is not None:
            values.append(s)
    return values


@dataclass
class Param:
    name: str
    cs_type: str
    optional: bool
    default: Any = UNKNOWN
    choices: Optional[list[str]] = None
    choices_enum: Optional[str] = None
    choices_extra: list[str] = field(default_factory=list)
    kind_hint: str = ""
    multi_attr: bool = False
    range: Optional[dict] = None
    mangled: str = ""


@dataclass
class SourceNode:
    name: str                  # display name, e.g. "Search.ByProperty"
    assembly: str
    namespace: str
    cls: str                   # class name (nested: Outer+Inner)
    method: str
    params: list[Param]
    outputs: list[str]
    output_kinds: list[str]    # kind hint per output ("" when none)
    return_type: str
    deprecated: bool = False
    hidden: bool = False
    converter: bool = False
    id: str = ""
    source: str = ""           # file name, for messages


@dataclass
class _TypeDecl:
    name: str
    kind: str
    public: bool
    generic: bool
    start: int
    body_start: int
    body_end: int
    decl_start: int
    parent: Optional["_TypeDecl"] = None
    attrs: list[Attribute] = field(default_factory=list)

    @property
    def full_name(self) -> str:
        return self.name if self.parent is None else self.parent.full_name + "+" + self.name

    @property
    def chain_public(self) -> bool:
        return self.public and (self.parent is None or self.parent.chain_public)


_TYPE_DECL_RE = re.compile(
    r"(?P<mods>(?:\b(?:public|internal|private|protected|static|sealed|abstract|partial|unsafe|readonly|ref|new|file)\s+)*)"
    r"\b(?P<kind>class|struct|enum|interface|record)\s+(?P<name>[A-Za-z_]\w*)\s*(?P<gen><)?"
)

_METHOD_RE = re.compile(
    r"\bpublic\s+static\s+(?:(?:unsafe|extern|new|async)\s+)*(?P<ret>(?:[^;{}()=]|\([^()]*\))+?)\s+(?P<name>[A-Za-z_]\w*)\s*(?P<gen><[^>()]*>)?\s*\("
)


@dataclass
class _FileIndex:
    path: Path
    text: str
    masked: str
    namespace: str
    types: list[_TypeDecl]


def _scan_file(path: Path) -> _FileIndex:
    text = path.read_text(encoding="utf-8-sig")
    masked = mask_source(text)
    ns_match = re.search(r"^\s*namespace\s+([\w.]+)\s*[;{]", masked, re.M)
    namespace = ns_match.group(1) if ns_match else ""
    types: list[_TypeDecl] = []
    for m in _TYPE_DECL_RE.finditer(masked):
        # find the body: first '{' (or ';' for a bodyless record/delegate) after the header
        i = m.end()
        depth_paren = 0
        body_start = -1
        while i < len(masked):
            c = masked[i]
            if c == "(":
                depth_paren += 1
            elif c == ")":
                depth_paren -= 1
            elif depth_paren == 0 and c == "{":
                body_start = i
                break
            elif depth_paren == 0 and c == ";":
                break
            i += 1
        if body_start < 0:
            continue
        body_end = _match_forward(masked, body_start)
        if body_end < 0:
            continue
        mods = m.group("mods") or ""
        words = set(mods.split())
        decl_start = m.start()
        generic = bool(m.group("gen"))
        types.append(_TypeDecl(
            name=m.group("name"), kind=m.group("kind"), public="public" in words, generic=generic,
            start=decl_start, body_start=body_start, body_end=body_end, decl_start=decl_start,
        ))
    # nesting: innermost enclosing declaration
    for t in types:
        enclosing = [o for o in types if o is not t and o.body_start < t.start < o.body_end]
        if enclosing:
            t.parent = max(enclosing, key=lambda o: o.body_start)
    # type-level visibility: only an explicit `public` counts; nested types without modifier are private
    for t in types:
        t.attrs = parse_attribute_groups(_attribute_groups_before(masked, text, t.decl_start))
    return _FileIndex(path, text, masked, namespace, types)


class SourceIndex:
    """Every zero-touch node method found in the sources, with the loader's id and port descriptions."""

    # Types the sources use by short name that the loader writes with a full name. Types declared in the sources
    # themselves are found by scanning; this lists the rest.
    EXTERNAL_TYPES = {
        # System
        "DateTime": "System.DateTime", "TimeSpan": "System.TimeSpan", "Guid": "System.Guid",
        "DateTimeOffset": "System.DateTimeOffset", "Exception": "System.Exception",
        "Uri": "System.Uri", "Version": "System.Version", "Array": "System.Array",
        # Navisworks API
        "Document": "Autodesk.Navisworks.Api.Document", "ModelItem": "Autodesk.Navisworks.Api.ModelItem",
        "ModelItemCollection": "Autodesk.Navisworks.Api.ModelItemCollection", "Model": "Autodesk.Navisworks.Api.Model",
        "SelectionSet": "Autodesk.Navisworks.Api.SelectionSet", "SavedItem": "Autodesk.Navisworks.Api.SavedItem",
        "SavedViewpoint": "Autodesk.Navisworks.Api.SavedViewpoint", "FolderItem": "Autodesk.Navisworks.Api.FolderItem",
        "GroupItem": "Autodesk.Navisworks.Api.GroupItem", "Units": "Autodesk.Navisworks.Api.Units",
        "DataProperty": "Autodesk.Navisworks.Api.DataProperty", "Viewpoint": "Autodesk.Navisworks.Api.Viewpoint",
        "BoundingBox3D": "Autodesk.Navisworks.Api.BoundingBox3D", "Point3D": "Autodesk.Navisworks.Api.Point3D",
        "Vector3D": "Autodesk.Navisworks.Api.Vector3D", "Color": "Autodesk.Navisworks.Api.Color",
        "ClashTest": "Autodesk.Navisworks.Api.Clash.ClashTest", "ClashResult": "Autodesk.Navisworks.Api.Clash.ClashResult",
        "ClashResultGroup": "Autodesk.Navisworks.Api.Clash.ClashResultGroup",
        "TimelinerTask": "Autodesk.Navisworks.Api.Timeliner.TimelinerTask",
        # BIMCamel exporter
        "CoordOptions": "BIMCamel.Ifc.CoordOptions", "SpatialNames": "BIMCamel.Ifc.SpatialNames",
        "PropertyRoles": "BIMCamel.Data.PropertyRoles", "ParamMapRule": "BIMCamel.Data.ParamMapRule",
    }
    GENERIC_SYSTEM = {
        "IEnumerable": "System.Collections.Generic.IEnumerable", "IList": "System.Collections.Generic.IList",
        "List": "System.Collections.Generic.List", "ICollection": "System.Collections.Generic.ICollection",
        "IReadOnlyList": "System.Collections.Generic.IReadOnlyList",
        "IReadOnlyCollection": "System.Collections.Generic.IReadOnlyCollection",
        "Dictionary": "System.Collections.Generic.Dictionary", "IDictionary": "System.Collections.Generic.IDictionary",
        "HashSet": "System.Collections.Generic.HashSet", "KeyValuePair": "System.Collections.Generic.KeyValuePair",
        "Nullable": "System.Nullable",
    }
    PLAIN_SYSTEM = {
        "IEnumerable": "System.Collections.IEnumerable", "IList": "System.Collections.IList",
        "IDictionary": "System.Collections.IDictionary", "ICollection": "System.Collections.ICollection",
        "Hashtable": "System.Collections.Hashtable",
    }
    KEYWORDS = {
        "bool", "byte", "sbyte", "char", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "decimal",
        "string", "object",
    }
    VALUE_TYPES = {
        "bool", "byte", "sbyte", "char", "short", "ushort", "int", "uint", "long", "ulong", "float", "double", "decimal",
        "DateTime", "TimeSpan", "Guid", "DateTimeOffset", "Units", "Color", "Point3D", "Vector3D", "BoundingBox3D",
    }

    def __init__(self) -> None:
        self.nodes: list[SourceNode] = []
        self._declared: dict[str, list[tuple[str, str]]] = {}  # simple name -> [(full name, kind)]
        self._delegates: set[str] = set()
        self._files: list[tuple[str, _FileIndex]] = []
        for assembly, root in SOURCE_DIRS:
            if not root.exists():
                continue
            for path in sorted(root.rglob("*.cs")):
                if EXCLUDED_PARTS.intersection(path.relative_to(root).parts):
                    continue
                self._files.append((assembly, _scan_file(path)))
        declaring = [fi for _, fi in self._files]
        for root in TYPE_ONLY_DIRS:
            if root.exists():
                for path in sorted(root.rglob("*.cs")):
                    if not EXCLUDED_PARTS.intersection(path.relative_to(root).parts):
                        declaring.append(_scan_file(path))
        for fi in declaring:
            for t in fi.types:
                self._declared.setdefault(t.name, []).append(((fi.namespace + "." if fi.namespace else "") + t.full_name, t.kind))
            for m in re.finditer(r"\bdelegate\s+[^;(]+?\s+([A-Za-z_]\w*)\s*[<(]", fi.masked):
                self._delegates.add(m.group(1))
        self.enums: dict[str, list[str]] = {}
        for fi in declaring:
            for t in fi.types:
                if t.kind == "enum":
                    members: list[str] = []
                    for a, b in _split_top_level(fi.masked, t.body_start + 1, t.body_end):
                        piece = fi.masked[a:b].strip()
                        piece = re.sub(r"\[[^\]]*\]", "", piece).strip()
                        mm = re.match(r"([A-Za-z_]\w*)", piece)
                        if mm:
                            members.append(mm.group(1))
                    self.enums[t.name] = members
        for assembly, fi in self._files:
            self._collect_nodes(assembly, fi)
        self.by_name: dict[str, SourceNode] = {}
        for n in self.nodes:
            if not n.deprecated and not n.hidden and not n.converter:
                self.by_name.setdefault(n.name, n)

    # ----- collecting

    def _collect_nodes(self, assembly: str, fi: _FileIndex) -> None:
        masked, text = fi.masked, fi.text
        # brace depth at every offset, to tell members from method bodies
        depth_at = [0] * (len(masked) + 1)
        d = 0
        for i, c in enumerate(masked):
            if c == "{":
                d += 1
            elif c == "}":
                d -= 1
            depth_at[i + 1] = d
        for t in fi.types:
            if t.kind != "class" or t.generic or not t.chain_public:
                continue
            class_hidden = False
            for a in t.attrs:
                if a.name == "IsVisibleInLibrary" and a.args and a.args[0].strip() == "false":
                    class_hidden = True
            for m in _METHOD_RE.finditer(masked, t.body_start + 1, t.body_end):
                if depth_at[m.start()] != depth_at[t.body_start + 1]:
                    continue  # inside a method body or a nested type
                ret = m.group("ret").strip()
                if "operator" in ret.split() or ret in ("class", "struct") or m.group("gen"):
                    continue
                open_index = m.end() - 1
                close = _match_forward(masked, open_index)
                if close < 0:
                    continue
                attrs = parse_attribute_groups(_attribute_groups_before(masked, text, m.start()))
                node = self._build_node(assembly, fi, t, m.group("name"), ret, attrs, open_index, close, class_hidden)
                if node is not None:
                    self.nodes.append(node)

    def _build_node(self, assembly: str, fi: _FileIndex, t: _TypeDecl, method: str, ret: str, attrs: list[Attribute],
                    open_index: int, close: int, class_hidden: bool) -> Optional[SourceNode]:
        masked, text = fi.masked, fi.text
        params: list[Param] = []
        for a, b in _split_top_level(masked, open_index + 1, close):
            if not masked[a:b].strip():
                continue
            parsed = self._parse_param(masked, text, a, b)
            if parsed is None:
                return None  # ref/out/params/delegate: the loader skips the method
            params.append(parsed)
        converter = _attr(attrs, "TypeConverterRegistration") is not None
        vis = _attr(attrs, "IsVisibleInLibrary")
        hidden = class_hidden or (vis is not None and bool(vis.args) and vis.args[0].strip() == "false")
        deprecated = _attr(attrs, "NodeDeprecated") is not None
        name_attr = _attr(attrs, "NodeName")
        name = (_string_args(name_attr) or [None])[0] if name_attr else None
        name = name or (t.full_name.replace("+", ".") + "." + method)
        multi = _attr(attrs, "MultiReturn")
        kinds_attr = _attr(attrs, "PortKinds")
        kinds = _string_args(kinds_attr) if kinds_attr else []
        if multi and "Dictionary" in ret:
            outputs = _string_args(multi)
            out_kinds = [kinds[i] if i < len(kinds) else "" for i in range(len(outputs))]
            out_types = ["object"] * len(outputs)
        elif ret == "void":
            outputs, out_kinds, out_types = ["result"], [""], [params[0].cs_type if params else "object"]
        else:
            ret_name = _attr(attrs, "NodeName", "return")
            outputs = [(_string_args(ret_name) or ["result"])[0] if ret_name else "result"]
            out_kinds, out_types = [""], [ret]
        node = SourceNode(
            name=name, assembly=assembly, namespace=fi.namespace, cls=t.full_name, method=method, params=params,
            outputs=outputs, output_kinds=out_kinds, return_type=out_types[0] if len(out_types) == 1 else "object",
            deprecated=deprecated, hidden=hidden, converter=converter, source=fi.path.name,
        )
        node.output_types = out_types  # type: ignore[attr-defined]
        for p in params:
            p.mangled = self.mangle(p.cs_type, fi)
        node.id = (
            (fi.namespace + "." if fi.namespace else "") + t.full_name + "." + method
            + (("@" + ",".join(p.mangled for p in params)) if params else "")
        )
        return node

    def _parse_param(self, masked: str, text: str, a: int, b: int) -> Optional[Param]:
        raw = text[a:b]
        mraw = masked[a:b]
        # leading attributes
        groups: list[str] = []
        pos = 0
        while True:
            m = re.match(r"\s*\[", mraw[pos:])
            if not m:
                break
            open_i = pos + m.end() - 1
            close_i = _match_forward(mraw, open_i)
            if close_i < 0:
                return None
            groups.append(raw[open_i + 1:close_i])
            pos = close_i + 1
        attrs = parse_attribute_groups(groups)
        rest_raw, rest_masked = raw[pos:], mraw[pos:]
        # default value
        default_text = None
        eq = None
        depth = 0
        for i, c in enumerate(rest_masked):
            if c in "([{<":
                depth += 1
            elif c in ")]}>":
                depth -= 1
            elif c == "=" and depth == 0:
                eq = i
                break
        if eq is not None:
            default_text = rest_raw[eq + 1:].strip()
            decl = rest_masked[:eq].strip()
        else:
            decl = rest_masked.strip()
        words = decl.split()
        if words and words[0] in ("params", "ref", "out", "in"):
            return None
        if words and words[0] == "this":
            decl = decl[len("this"):].strip()
        nm = re.search(r"(@?[A-Za-z_]\w*)\s*$", decl)
        if not nm:
            return None
        pname = nm.group(1).lstrip("@")
        ptype = decl[: nm.start()].strip()
        # the original text keeps the same offsets: take the type from the masked decl (it has no literals)
        base_type = re.sub(r"\s+", " ", ptype).replace(" ?", "?").replace("< ", "<").replace(" >", ">").replace(" ,", ",")
        simple = re.sub(r"\?+$", "", base_type)
        root = simple.split("<")[0].split(".")[-1]
        if root in ("Func", "Action", "Predicate", "Comparison", "Converter", "EventHandler") or root in self._delegates:
            return None
        param = Param(pname, base_type, optional=eq is not None)
        if eq is not None:
            param.default = eval_default(default_text or "")
        ch = _attr(attrs, "NodeChoices")
        if ch is not None:
            param.choices = _string_args(ch)
        ce = _attr(attrs, "NodeChoicesFromEnum")
        if ce is not None and ce.args:
            tm = re.match(r"typeof\(\s*([\w.]+)\s*\)", ce.args[0])
            param.choices_enum = tm.group(1).split(".")[-1] if tm else ""
            param.choices = None
            param.choices_extra = _string_args(Attribute("x", ce.args[1:]))
        kind = _attr(attrs, "PortKinds")
        if kind is not None:
            ks = _string_args(kind)
            param.kind_hint = ks[0] if ks else ""
        param.multi_attr = _attr(attrs, "MultiInput") is not None
        rg = _attr(attrs, "NodeRange")
        if rg is not None:
            rng: dict[str, Any] = {}
            positional = [x for x in rg.args if not re.match(r"^[A-Za-z_]\w*\s*=(?!=)", x)]
            if len(positional) >= 2:
                rng["min"], rng["max"] = parse_cs_number(positional[0]), parse_cs_number(positional[1])
            for x in rg.args:
                nm2 = re.match(r"^([A-Za-z_]\w*)\s*=(?!=)\s*(.+)$", x)
                if nm2 and nm2.group(1) in ("SoftMin", "SoftMax", "Step", "Unit"):
                    rng[nm2.group(1).lower()] = parse_cs_number(nm2.group(2)) if nm2.group(1) != "Unit" else parse_cs_string_literals(nm2.group(2))
            param.range = rng
        return param

    # ----- type names

    def _resolve_declared(self, simple: str, fi: Optional[_FileIndex]) -> Optional[str]:
        found = self._declared.get(simple)
        if not found:
            return None
        if len(found) == 1:
            return found[0][0]
        if fi is not None:
            for full, _ in found:
                if full.startswith(fi.namespace + "."):
                    return full
        return found[0][0]

    def is_enum(self, type_text: str) -> bool:
        simple = type_text.rstrip("?").split(".")[-1]
        return any(kind == "enum" for _, kind in self._declared.get(simple, []))

    def is_value_type(self, type_text: str) -> bool:
        simple = type_text.rstrip("?").split(".")[-1]
        if simple in self.VALUE_TYPES or self.is_enum(simple):
            return True
        return any(kind == "struct" for _, kind in self._declared.get(simple, []))

    def mangle(self, type_text: str, fi: Optional[_FileIndex] = None) -> str:
        """The text the loader writes for a parameter type (AssemblyNodeLoader.GetMangledTypeName)."""
        t = type_text.strip().replace(" ", "")
        if t.endswith("[]"):
            return self.mangle(t[:-2], fi) + "[]"
        if t.endswith("?"):
            inner = t[:-1]
            return self.mangle(inner, fi) + "?" if self.is_value_type(inner) else self.mangle(inner, fi)
        if "<" in t:
            head, args = t[: t.index("<")], t[t.index("<") + 1: t.rindex(">")]
            parts = [args[a:b] for a, b in _split_top_level(args, 0, len(args))]
            short = head.split(".")[-1]
            full = self.GENERIC_SYSTEM.get(short)
            if full is None:
                full = self._resolve_declared(short, fi) or head
            if short == "Nullable":
                return self.mangle(parts[0], fi) + "?"
            return full + "<" + ",".join(self.mangle(p, fi) for p in parts) + ">"
        if t in self.KEYWORDS:
            return t
        short = t.split(".")[-1]
        if "." in t and short not in self._declared:
            return t  # already written with a namespace
        declared = self._resolve_declared(short, fi)
        if declared:
            return declared
        if short in self.PLAIN_SYSTEM:
            return self.PLAIN_SYSTEM[short]
        if short in self.EXTERNAL_TYPES:
            return self.EXTERNAL_TYPES[short]
        raise SpecError(
            "cannot work out the full name of the C# type '" + type_text + "' (add it to EXTERNAL_TYPES in tools/wiki_graph.py, "
            "or regenerate the catalogue with ids)")


_INDEX: Optional[SourceIndex] = None


def source_index() -> SourceIndex:
    global _INDEX
    if _INDEX is None:
        _INDEX = SourceIndex()
    return _INDEX


# ===================================================================== kinds, editors and node definitions

NUMBER_KEYWORDS = {"double", "float", "decimal"}
INTEGER_KEYWORDS = {"int", "long", "short", "byte", "sbyte", "ushort", "uint", "ulong"}
FAMILY_BY_TYPE = {
    "Double": "number", "Single": "number", "Decimal": "number",
    "Int32": "integer", "Int64": "integer", "Int16": "integer", "Byte": "integer", "SByte": "integer",
    "UInt16": "integer", "UInt32": "integer", "UInt64": "integer",
    "Boolean": "boolean", "String": "text", "Char": "text",
    "DateTime": "datetime", "TimeSpan": "datetime", "DateTimeOffset": "datetime", "TimelinerTask": "datetime",
    "TimelinerTaskCollection": "datetime", "TimelinerTaskType": "datetime",
    "Color": "colour", "Colour": "colour", "Brush": "colour", "SolidColorBrush": "colour",
    "Point": "geometry", "Point2D": "geometry", "Point3D": "geometry", "Vector": "geometry", "Vector3D": "geometry",
    "Vector3": "geometry", "BoundingBox": "geometry", "BoundingBox3D": "geometry", "Box": "geometry",
    "Transform3D": "geometry", "Rotation3D": "geometry", "Matrix3D": "geometry", "Line": "geometry", "Plane": "geometry",
    "ModelItem": "item", "Model": "item", "Units": "item",
    "ModelItemCollection": "selection", "SelectionSet": "selection", "SelectionSource": "selection", "Search": "selection",
    "Selection": "selection",
    "Viewpoint": "viewpoint", "SavedViewpoint": "viewpoint", "SavedItem": "viewpoint", "FolderItem": "viewpoint",
    "GroupItem": "viewpoint", "Camera": "viewpoint", "SavedViewpointAnimation": "viewpoint",
    "SavedViewpointAnimationCut": "viewpoint",
    "ClashResult": "clash", "ClashTest": "clash", "ClashResultGroup": "clash", "ClashResultGroupBase": "clash",
    "ClashResultGroupCollection": "clash",
    "Document": "document", "DocumentModels": "document",
    "IDictionary": "data", "Dictionary": "data", "DataProperty": "data", "PropertyCategory": "data",
    "ParamMapRule": "data", "DyncameloTable": "data",
    "IWorkflowAction": "action",
}
FAMILY_BY_KEYWORD = {
    **{k: "number" for k in NUMBER_KEYWORDS}, **{k: "integer" for k in INTEGER_KEYWORDS},
    "bool": "boolean", "string": "text", "char": "text",
}
LIST_TYPES = {"List", "IList", "IEnumerable", "ICollection", "IReadOnlyList", "IReadOnlyCollection"}
NUMERIC_FAMILIES = {"number", "integer"}
KNOWN_FAMILIES = {
    "any", "number", "integer", "boolean", "text", "datetime", "colour", "geometry", "item", "selection", "viewpoint",
    "clash", "document", "data", "file", "action",
}


@dataclass
class PortInfo:
    name: str
    family: str = "any"            # colour family of the socket (PortFamily, lower case)
    depth: str = "unknown"         # item | list | nested | unknown
    editor: str = "none"           # none | number | toggle | choice | text | colour | path | model
    has_default: bool = False
    default: Any = UNKNOWN
    choices: Optional[list[str]] = None
    multi: bool = False
    range: Optional[dict] = None
    value_type: str = ""           # integer | float | "" for number editors
    type_text: str = ""


@dataclass
class NodeDef:
    name: str                      # name shown on the library and, by default, on the node
    node_type: str                 # "ZeroTouch" or the interactive type tag
    inputs: list[PortInfo]
    outputs: list[PortInfo]
    interactive: bool = False
    kind: str = ""                 # key in INTERACTIVE for built-in kinds
    definition_id: str = ""
    assembly: str = ""
    category: str = ""


def _strip_nullable(t: str) -> str:
    return t[:-1] if t.endswith("?") else t


def _list_element(t: str) -> Optional[str]:
    t = _strip_nullable(t.strip().replace(" ", ""))
    if t in ("string", "object"):
        return None
    if t.endswith("[]"):
        return t[:-2]
    if "<" in t:
        head, args = t[: t.index("<")].split(".")[-1], t[t.index("<") + 1: t.rindex(">")]
        parts = [args[a:b] for a, b in _split_top_level(args, 0, len(args))]
        if head in LIST_TYPES and len(parts) == 1:
            return parts[0]
    return None


def _family_of_scalar(t: str, index: SourceIndex) -> str:
    t = _strip_nullable(t.strip().replace(" ", ""))
    head = t.split("<")[0].split(".")[-1]
    if t in FAMILY_BY_KEYWORD:
        return FAMILY_BY_KEYWORD[t]
    if index.is_enum(head):
        return "integer"
    if len(head) > 9 and head.startswith("Dyncamelo"):
        head = head[len("Dyncamelo"):]
    if head in FAMILY_BY_TYPE:
        return FAMILY_BY_TYPE[head]
    if head in ("DateTime",):
        return "datetime"
    return "any"


def kind_of_type(type_text: str, index: SourceIndex) -> tuple[str, str]:
    """(family, depth) of a C# type, following PortKinds.FromType."""
    depth = 0
    current = type_text.strip().replace(" ", "")
    while depth < 3:
        element = _list_element(current)
        if element is None:
            break
        depth += 1
        current = _strip_nullable(element)
    family = _family_of_scalar(current, index)
    if depth == 0:
        return family, ("unknown" if family == "any" and _strip_nullable(current) == "object" else "item")
    shape = "list" if depth == 1 else "nested"
    return family, shape


def parse_kind_hint(hint: str) -> Optional[tuple[str, str]]:
    """PortKinds.TryParse: 'viewpoint*' is a list of viewpoints, 'text**' a list of lists of text."""
    h = hint.strip()
    if not h:
        return None
    stars = len(h) - len(h.rstrip("*"))
    family = h.rstrip("*").strip().lower()
    if family not in KNOWN_FAMILIES:
        return None
    depth = "item" if stars == 0 else "list" if stars == 1 else "nested"
    if family == "any" and stars == 0:
        depth = "unknown"
    return family, depth


def _looks_like_path(name: str) -> bool:
    n = name.lower()
    return n.endswith(("path", "file", "folder", "directory", "filename"))


def _is_folder_name(name: str) -> bool:
    return name.lower().endswith(("folder", "directory", "dir"))


def _model_item_port(type_text: str) -> bool:
    t = _strip_nullable(type_text.strip().replace(" ", ""))
    if t.split(".")[-1] == "ModelItemCollection":
        return True
    element = _list_element(t) or t
    return _strip_nullable(element).split(".")[-1] == "ModelItem"


def port_from_param(p: Param, index: SourceIndex) -> PortInfo:
    choices = p.choices
    if choices is None and p.choices_enum is not None:
        members = index.enums.get(p.choices_enum)
        choices = (list(p.choices_extra) + list(members)) if members is not None else None
    elif choices is None and index.is_enum(p.cs_type):
        choices = list(index.enums.get(_strip_nullable(p.cs_type).split(".")[-1], [])) or None
    hint = parse_kind_hint(p.kind_hint)
    family, depth = hint if hint else kind_of_type(p.cs_type, index)
    if not hint and family == "text" and _looks_like_path(p.name):
        family = "file"
    info = PortInfo(
        name=p.name, family=family, depth=depth, has_default=p.optional, default=p.default,
        choices=choices if choices else None, multi=False, range=p.range, type_text=p.cs_type,
    )
    # multi-input only applies to list-typed parameters
    info.multi = p.multi_attr and _list_element(p.cs_type) is not None
    # editor (PortEditors.Resolve)
    if info.choices:
        info.editor = "choice"
    elif _model_item_port(p.cs_type):
        info.editor = "model"
    elif depth != "item":
        info.editor = "none"
    elif family == "boolean":
        info.editor = "toggle"
    elif family in NUMERIC_FAMILIES:
        info.editor = "number"
        base = _strip_nullable(p.cs_type.strip())
        info.value_type = "integer" if (base in INTEGER_KEYWORDS or family == "integer") else "float"
    elif family == "colour":
        info.editor = "colour"
    elif family == "file":
        info.editor = "path"
    elif family in ("text", "datetime"):
        info.editor = "text"
    return info


def _node_def_from_source(src: SourceNode, index: SourceIndex) -> NodeDef:
    inputs = [port_from_param(p, index) for p in src.params]
    outputs: list[PortInfo] = []
    out_types = getattr(src, "output_types", [src.return_type] * len(src.outputs))
    for name, kind, ty in zip(src.outputs, src.output_kinds, out_types):
        hint = parse_kind_hint(kind) if kind else None
        family, depth = hint if hint else kind_of_type(ty, index)
        outputs.append(PortInfo(name=name, family=family, depth=depth, type_text=ty))
    return NodeDef(
        name=src.name, node_type="ZeroTouch", inputs=inputs, outputs=outputs, definition_id=src.id, assembly=src.assembly,
    )


def _p(name: str, family: str = "any", depth: str = "unknown", **kw: Any) -> PortInfo:
    return PortInfo(name=name, family=family, depth=depth, **kw)


# Built-in (interactive) node kinds: the NodeModel subclasses in src/Dyncamelo.Core/Nodes and src/Dyncamelo.Nodes.
# `type` is the NodeType tag the file stores, `data` the fields of its Data object, in the order SerializeData writes them.
INTERACTIVE: dict[str, dict] = {
    "String": dict(type="StringInput", outputs=[_p("value", "text", "item")], category="Input"),
    "Number": dict(type="NumberInput", outputs=[_p("value", "number", "item")], category="Input"),
    "Integer": dict(type="IntegerInput", outputs=[_p("value", "integer", "item")], category="Input"),
    "Boolean": dict(type="BooleanToggle", outputs=[_p("value", "boolean", "item")], category="Input"),
    "Choice": dict(type="ChoiceInput", outputs=[_p("value", "text", "item"), _p("index", "integer", "item")], category="Input"),
    "Date": dict(type="DateInput", outputs=[_p("value", "datetime", "item")], category="Input"),
    "Number Slider": dict(type="NumberSlider", outputs=[_p("value", "number", "item")], category="Input"),
    "Integer Slider": dict(type="IntegerSlider", outputs=[_p("value", "integer", "item")], category="Input"),
    "File Path": dict(type="FilePath", outputs=[_p("path", "file", "item")], category="Input"),
    "Directory Path": dict(type="DirectoryPath", outputs=[_p("path", "file", "item")], category="Input"),
    "Color Picker": dict(type="ColorPicker", outputs=[_p("color", "colour", "item")], category="Color"),
    "Watch": dict(type="Watch", inputs=[_p("value")], outputs=[_p("value")], category="Display"),
    "Watch List": dict(type="WatchList", inputs=[_p("list")], outputs=[_p("list")], category="Display"),
    "Watch Table": dict(type="WatchTable", inputs=[_p("table")], outputs=[_p("table")], category="Display"),
    "Watch Image": dict(type="WatchImage", inputs=[_p("imagePath")], outputs=[_p("imagePath")], category="Display"),
    "List.Create": dict(type="ListCreate", outputs=[_p("list", "any", "list")], category="List"),
    "Loop.Item": dict(
        type="LoopItem", inputs=[_p("items", "any", "list")],
        outputs=[_p("item"), _p("index", "integer", "item"), _p("count", "integer", "item"), _p("loop")], category="Workflow"),
    "Loop.Collect": dict(
        type="LoopCollect", inputs=[_p("loop"), _p("value")], outputs=[_p("results", "any", "list")], category="Workflow"),
}
# Node kinds whose body (editor, display area) adds height on the canvas, in pixels (estimates for the layout).
BODY_HEIGHT = {
    "String": 40, "Number": 40, "Integer": 40, "Boolean": 36, "Choice": 112, "Date": 40, "Number Slider": 52,
    "Integer Slider": 52, "File Path": 44, "Directory Path": 44, "Color Picker": 44, "Watch": 84, "Watch List": 110,
    "Watch Table": 150, "Watch Image": 120, "List.Create": 34,
}
WIDE_KINDS = {"Choice", "File Path", "Directory Path", "Watch List", "Watch Table", "Watch Image"}
PLAYER_INPUT_KINDS = {
    "String", "Number", "Integer", "Boolean", "Choice", "Date", "Number Slider", "Integer Slider", "File Path",
    "Directory Path", "Color Picker",
}
PLAYER_OUTPUT_KINDS = {"Watch", "Watch List", "Watch Table", "Watch Image"}


class Catalogue:
    """docs/dyncamelo-nodes.json: the names, ports and descriptions the wiki and the editor's library agree on."""

    def __init__(self, path: Path = CATALOGUE) -> None:
        data = json.loads(path.read_text(encoding="utf-8"))
        self.nodes = {n["name"]: n for n in data["nodes"]}
        self.version = data.get("version", "")

    def names(self) -> list[str]:
        return sorted(self.nodes)


_CATALOGUE: Optional[Catalogue] = None


def catalogue() -> Catalogue:
    global _CATALOGUE
    if _CATALOGUE is None:
        _CATALOGUE = Catalogue()
    return _CATALOGUE


def resolve_node(name: str) -> NodeDef:
    """The definition a spec's `node` names: a built-in kind or a catalogue node, checked against the sources."""
    if name in INTERACTIVE:
        spec = INTERACTIVE[name]
        return NodeDef(
            name=name, node_type=spec["type"], inputs=[PortInfo(**vars(p)) for p in spec.get("inputs", [])],
            outputs=[PortInfo(**vars(p)) for p in spec["outputs"]], interactive=True, kind=name, category=spec["category"],
        )
    cat = catalogue()
    index = source_index()
    entry = cat.nodes.get(name)
    if entry is None:
        close = difflib.get_close_matches(name, cat.names() + sorted(INTERACTIVE), n=4, cutoff=0.6)
        raise SpecError(
            "unknown node '" + name + "'" + ((" - did you mean " + ", ".join("'" + c + "'" for c in close) + "?") if close else "")
            + " (nodes must be in docs/dyncamelo-nodes.json; retired nodes are not)")
    if entry.get("interactive"):
        raise SpecError("the node '" + name + "' is interactive and not supported by this tool; use another node")
    src = index.by_name.get(name)
    if src is None:
        raise SpecError("the node '" + name + "' is in the catalogue but its C# method was not found in the sources")
    definition = _node_def_from_source(src, index)
    cat_in = [i["name"] for i in entry.get("inputs", [])]
    cat_out = [o["name"] for o in entry.get("outputs", [])]
    if cat_in != [p.name for p in definition.inputs] or cat_out != [p.name for p in definition.outputs]:
        raise SpecError(
            "the catalogue and the sources disagree about the ports of '" + name + "' (catalogue " + str(cat_in) + " -> " + str(cat_out)
            + ", sources " + str([p.name for p in definition.inputs]) + " -> " + str([p.name for p in definition.outputs])
            + "); regenerate the catalogue with tools/generate_node_catalog.py")
    cat_id = entry.get("id")
    if cat_id and cat_id != definition.definition_id:
        raise SpecError(
            "the catalogue id of '" + name + "' (" + cat_id + ") differs from the id derived from the sources ("
            + definition.definition_id + ")")
    definition.category = entry.get("category", "")
    return definition


def node_summary(name: str) -> str:
    d = resolve_node(name)
    lines = [d.name + "  [" + d.node_type + "]"]
    if d.definition_id:
        lines.append("  id: " + d.definition_id)
        lines.append("  assembly: " + d.assembly)
    lines.append("  inputs:")
    for p in d.inputs:
        bits = [p.family + "/" + p.depth, "editor=" + p.editor]
        if p.has_default:
            bits.append("default=" + ("?" if p.default is UNKNOWN else json.dumps(p.default, default=str)))
        else:
            bits.append("required")
        if p.choices:
            bits.append("choices=" + "|".join(p.choices))
        if p.multi:
            bits.append("multi-input")
        if p.range:
            bits.append("range=" + json.dumps({k: v for k, v in p.range.items() if v is not UNKNOWN}, default=str))
        lines.append("    " + p.name + "  (" + ", ".join(bits) + ")")
    lines.append("  outputs:")
    for p in d.outputs:
        lines.append("    " + p.name + "  (" + p.family + "/" + p.depth + ")")
    return "\n".join(lines)


# ===================================================================== spec -> graph

FRAME_COLOURS = {
    "Blue": "#FF3D6A99", "Green": "#FF3F7249", "Amber": "#FF9A7B2D", "Red": "#FF8A3B3B", "Purple": "#FF6B4E8E",
    "Gray": "#FF5A6273",
}
LACING_MODES = ("Auto", "Shortest", "Longest", "CrossProduct")
RUN_TYPES = ("Manual", "Automatic")
NODE_KEYS = {
    "id", "node", "title", "values", "value", "options", "min", "max", "step", "count", "text", "lacing", "player",
    "player_inputs",
}
TOP_KEYS = {"name", "description", "run", "nodes", "wires", "notes", "frames", "appVersion"}

# canvas geometry (estimates of how the editor draws a node; see the layout section)
HEADER_H, ROW_H, OUT_ROW_H, CHIP_H, PAD_H = 36, 30, 26, 22, 12
NODE_W, WIDE_W = 260, 290
COLUMN_GAP, ROW_GAP = 90, 40
NOTE_W = 320


@dataclass
class BNode:
    key: str
    kind_name: str
    defn: NodeDef
    title: str
    index: int
    values: dict[str, Any] = field(default_factory=dict)       # validated typed values by port name (UserValue)
    data: dict[str, Any] = field(default_factory=dict)         # interactive node payload
    lacing: str = "Auto"
    player: Optional[bool] = None
    player_inputs: list[str] = field(default_factory=list)
    wired_in: dict[str, list[tuple["BNode", str]]] = field(default_factory=dict)
    x: float = 0.0
    y: float = 0.0
    column: int = 0

    def in_port(self, name: str) -> Optional[PortInfo]:
        return next((p for p in self.defn.inputs if p.name == name), None)

    def out_port(self, name: str) -> Optional[PortInfo]:
        return next((p for p in self.defn.outputs if p.name == name), None)


@dataclass
class Wire:
    src: BNode
    src_port: str
    dst: BNode
    dst_port: str


def _is_number(v: Any) -> bool:
    return isinstance(v, (int, float)) and not isinstance(v, bool) and math.isfinite(v)


def _check_keys(obj: dict, allowed: set[str], where: str) -> None:
    extra = sorted(set(obj) - allowed)
    if extra:
        close = difflib.get_close_matches(extra[0], sorted(allowed), n=1)
        raise SpecError(where + ": unknown field '" + extra[0] + "'" + (" (did you mean '" + close[0] + "'?)" if close else ""))


def _normalise_colour(value: Any, where: str) -> str:
    if not isinstance(value, str) or not re.fullmatch(r"#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})", value):
        raise SpecError(where + ": a colour is written \"#RRGGBB\" (or \"#AARRGGBB\"), not " + json.dumps(value))
    digits = value[1:].upper()
    return "#" + ("FF" + digits if len(digits) == 6 else digits)


def _coerce_number(value: Any, port: PortInfo, where: str) -> Any:
    if not _is_number(value):
        raise SpecError(where + ": expects a number, not " + json.dumps(value))
    if port.value_type == "integer":
        if float(value) != round(float(value)):
            raise SpecError(where + ": expects a whole number, not " + json.dumps(value))
        out: Any = int(round(float(value)))
    else:
        out = float(value)
    if port.range:
        lo, hi = port.range.get("min"), port.range.get("max")
        if _is_number(lo) and out < lo:
            raise SpecError(where + ": " + json.dumps(value) + " is below the minimum " + json.dumps(lo))
        if _is_number(hi) and out > hi:
            raise SpecError(where + ": " + json.dumps(value) + " is above the maximum " + json.dumps(hi))
    return out


def _validate_typed_value(node: BNode, port: PortInfo, value: Any) -> tuple[bool, Any]:
    """(store, value): what the editor would pin for this typed value; store is False when it equals the default."""
    where = "node '" + node.key + "' input '" + port.name + "'"
    editor = port.editor
    if editor == "none":
        raise SpecError(
            where + " has no inline editor (its type is " + port.type_text + "): wire an input node such as String or Number into it")
    if editor == "model":
        raise SpecError(where + " takes model elements picked in Navisworks; wire a search or selection node into it")
    if editor == "choice":
        if not isinstance(value, str) or value not in (port.choices or []):
            raise SpecError(where + " must be one of " + ", ".join(json.dumps(c) for c in (port.choices or [])) + ", not " + json.dumps(value))
        return True, value
    if editor == "toggle":
        if not isinstance(value, bool):
            raise SpecError(where + " expects true or false, not " + json.dumps(value))
        return not (port.has_default and port.default is value), value
    if editor == "number":
        coerced = _coerce_number(value, port, where)
        same = port.has_default and _is_number(port.default) and float(port.default) == float(coerced)
        return not same, coerced
    if editor == "colour":
        return True, _normalise_colour(value, where)
    if editor in ("text", "path"):
        if not isinstance(value, str):
            raise SpecError(where + " expects text, not " + json.dumps(value))
        if value == "" and not port.has_default:
            raise SpecError(where + ": an empty text is the same as no value; leave it out")
        same = port.has_default and isinstance(port.default, str) and port.default == value
        return not same, value
    raise SpecError(where + ": cannot type a value here")


def _build_interactive_data(node: BNode, raw: dict) -> None:
    kind = node.kind_name
    where = "node '" + node.key + "' (" + kind + ")"

    def need(field_name: str) -> Any:
        if field_name not in raw:
            raise SpecError(where + " needs a '" + field_name + "'")
        return raw[field_name]

    def forbid(*names: str) -> None:
        for n in names:
            if n in raw:
                raise SpecError(where + " does not take '" + n + "'")

    allowed_common = {"id", "node", "title", "lacing", "player"}
    if kind == "String":
        v = need("value")
        if not isinstance(v, str):
            raise SpecError(where + ": 'value' must be text")
        node.data = {"Value": v}
        allowed = {"value"}
    elif kind == "Number":
        v = need("value")
        if not _is_number(v):
            raise SpecError(where + ": 'value' must be a number")
        node.data = {"Value": float(v)}
        allowed = {"value"}
    elif kind == "Integer":
        v = need("value")
        if not _is_number(v) or float(v) != round(float(v)):
            raise SpecError(where + ": 'value' must be a whole number")
        node.data = {"Value": int(round(float(v)))}
        allowed = {"value"}
    elif kind == "Boolean":
        v = need("value")
        if not isinstance(v, bool):
            raise SpecError(where + ": 'value' must be true or false")
        node.data = {"Value": v}
        allowed = {"value"}
    elif kind == "Choice":
        options = need("options")
        v = need("value")
        if not isinstance(options, list) or not options or not all(isinstance(o, str) and o.strip() and "\n" not in o for o in options):
            raise SpecError(where + ": 'options' must be a list of non-empty one-line texts")
        if len(set(o.strip() for o in options)) != len(options):
            raise SpecError(where + ": 'options' has repeats")
        if v not in options:
            raise SpecError(where + ": 'value' " + json.dumps(v) + " is not one of the options")
        node.data = {"Options": "\n".join(options), "Value": v}
        allowed = {"options", "value"}
    elif kind == "Date":
        v = need("value")
        if not isinstance(v, str) or not re.fullmatch(r"\d{4}-\d{2}-\d{2}( \d{2}:\d{2})?", v):
            raise SpecError(where + ": 'value' is a date like 2026-10-01 or 2026-10-01 14:30")
        node.data = {"Text": v}
        allowed = {"value"}
    elif kind in ("Number Slider", "Integer Slider"):
        integer = kind == "Integer Slider"
        v, lo, hi, step = need("value"), need("min"), need("max"), need("step")
        for label, x in (("value", v), ("min", lo), ("max", hi), ("step", step)):
            if not _is_number(x) or (integer and float(x) != round(float(x))):
                raise SpecError(where + ": '" + label + "' must be a " + ("whole number" if integer else "number"))
        if not lo < hi or not lo <= v <= hi or step <= 0:
            raise SpecError(where + ": needs min < max, min <= value <= max and step > 0")
        conv = (lambda x: int(round(float(x)))) if integer else float
        node.data = {"Value": conv(v), "Min": conv(lo), "Max": conv(hi), "Step": conv(step)}
        allowed = {"value", "min", "max", "step"}
    elif kind in ("File Path", "Directory Path"):
        v = need("value")
        if not isinstance(v, str):
            raise SpecError(where + ": 'value' must be text")
        node.data = {"Path": v}
        allowed = {"value"}
    elif kind == "Color Picker":
        c = _normalise_colour(need("value"), where)
        node.data = {"A": int(c[1:3], 16), "R": int(c[3:5], 16), "G": int(c[5:7], 16), "B": int(c[7:9], 16)}
        allowed = {"value"}
    elif kind in ("Watch", "Watch List", "Watch Table", "Watch Image"):
        node.data = {"ViewWidth": 0.0, "ViewHeight": 0.0}
        allowed = set()
    elif kind == "List.Create":
        count = raw.get("count")
        if not isinstance(count, int) or isinstance(count, bool) or count < 1:
            raise SpecError(where + " needs 'count': how many items the list has (1 or more)")
        node.defn.inputs = [_p("item" + str(i), "any", "unknown") for i in range(count)]
        node.data = {"ItemCount": count}
        allowed = {"count"}
    else:  # Loop.Item, Loop.Collect
        node.data = {}
        allowed = set()
    unknown = sorted(set(raw) - allowed - allowed_common - {"player_inputs"})
    if unknown:
        raise SpecError(where + " does not take '" + unknown[0] + "'")
    if "values" in raw:
        raise SpecError(where + " has no inputs to type values into; use 'value'")
    forbid()


def _parse_endpoint(text: str, where: str) -> tuple[str, Optional[str]]:
    text = text.strip()
    m = re.fullmatch(r"([A-Za-z0-9_-]+)(?:\.(.+))?", text)
    if not m:
        raise SpecError(where + ": '" + text + "' is not 'node.port'")
    return m.group(1), m.group(2)


def _compat_warning(src: BNode, sp: PortInfo, dst: BNode, dp: PortInfo) -> Optional[str]:
    fa, fb = sp.family, dp.family
    if fa == "any" or fb == "any" or fa == fb:
        return None
    if fa in NUMERIC_FAMILIES and fb in NUMERIC_FAMILIES:
        return None
    if {fa, fb} <= {"item", "selection"}:
        return None
    if {fa, fb} <= {"text", "file"}:
        return None
    if {fa, fb} <= {"clash", "viewpoint"}:
        return None  # a clash result is a saved item
    return (
        "wire " + src.key + "." + sp.name + " -> " + dst.key + "." + dp.name + " joins a " + fa + " output to a " + fb
        + " input; the editor draws it as a loose connection")


def _sort_key(s: str) -> str:
    return s.lower()


def build_nodes_and_wires(spec: dict, stem: str) -> tuple[list[BNode], list[Wire], list[str]]:
    warnings: list[str] = []
    raw_nodes = spec.get("nodes")
    if not isinstance(raw_nodes, list) or not raw_nodes:
        raise SpecError("'nodes' must be a non-empty list")
    nodes: list[BNode] = []
    by_key: dict[str, BNode] = {}
    notes_from_nodes: list[dict] = []
    for i, raw in enumerate(raw_nodes):
        if not isinstance(raw, dict):
            raise SpecError("nodes[" + str(i) + "] must be an object")
        _check_keys(raw, NODE_KEYS, "nodes[" + str(i) + "]")
        key, kind_name = raw.get("id"), raw.get("node")
        if not isinstance(key, str) or not re.fullmatch(r"[A-Za-z0-9_-]+", key or ""):
            raise SpecError("nodes[" + str(i) + "]: 'id' must be a short name made of letters, digits, '-' and '_'")
        if key in by_key:
            raise SpecError("node id '" + key + "' is used twice")
        if not isinstance(kind_name, str):
            raise SpecError("node '" + key + "': 'node' must name a node")
        if kind_name == "Note":
            notes_from_nodes.append({"text": raw.get("text"), "at": raw.get("at", "top")})
            continue
        defn = resolve_node(kind_name)
        title = raw.get("title", defn.name)
        if not isinstance(title, str) or not title.strip():
            raise SpecError("node '" + key + "': 'title' must be a text")
        bn = BNode(key=key, kind_name=kind_name, defn=defn, title=title, index=len(nodes))
        lacing = raw.get("lacing", "Auto")
        if lacing not in LACING_MODES:
            raise SpecError("node '" + key + "': 'lacing' is one of " + ", ".join(LACING_MODES))
        bn.lacing = lacing
        if "player" in raw:
            if not isinstance(raw["player"], bool):
                raise SpecError("node '" + key + "': 'player' is true or false")
            bn.player = raw["player"]
        if defn.interactive:
            _build_interactive_data(bn, raw)
        else:
            for f in ("value", "options", "min", "max", "step", "count", "text"):
                if f in raw:
                    raise SpecError("node '" + key + "' (" + kind_name + ") does not take '" + f + "'; type inputs under 'values'")
            values = raw.get("values", {})
            if not isinstance(values, dict):
                raise SpecError("node '" + key + "': 'values' must be an object of port: value")
            for port_name, v in values.items():
                port = bn.in_port(port_name)
                if port is None:
                    close = difflib.get_close_matches(port_name, [p.name for p in defn.inputs], n=1)
                    raise SpecError(
                        "node '" + key + "' (" + kind_name + ") has no input '" + port_name + "'"
                        + (" - did you mean '" + close[0] + "'?" if close else "")
                        + " (inputs: " + ", ".join(p.name for p in defn.inputs) + ")")
                store, pinned = _validate_typed_value(bn, port, v)
                if store:
                    bn.values[port_name] = pinned
                else:
                    warnings.append("node '" + key + "' input '" + port_name + "': " + json.dumps(v) + " is the default, so it is not stored")
        pi = raw.get("player_inputs", [])
        if not isinstance(pi, list) or not all(isinstance(x, str) for x in pi):
            raise SpecError("node '" + key + "': 'player_inputs' is a list of input names")
        for x in pi:
            port = bn.in_port(x)
            if port is None:
                raise SpecError("node '" + key + "': 'player_inputs' names '" + x + "', which is not an input")
            if port.editor == "none":
                raise SpecError("node '" + key + "': input '" + x + "' has no inline editor, so the Player cannot offer it")
        bn.player_inputs = list(pi)
        nodes.append(bn)
        by_key[key] = bn
    spec["_notes_from_nodes"] = notes_from_nodes

    wires: list[Wire] = []
    seen: set[tuple[str, str, str, str]] = set()
    for i, text in enumerate(spec.get("wires", [])):
        where = "wires[" + str(i) + "] " + json.dumps(text)
        if not isinstance(text, str) or "->" not in text:
            raise SpecError(where + ": write \"from.port -> to.port\"")
        left, right = text.split("->", 1)
        sk, sp = _parse_endpoint(left, where)
        dk, dp = _parse_endpoint(right, where)
        if sk not in by_key or dk not in by_key:
            raise SpecError(where + ": there is no node '" + (sk if sk not in by_key else dk) + "'")
        src, dst = by_key[sk], by_key[dk]
        if sp is None:
            if len(src.defn.outputs) != 1:
                raise SpecError(where + ": '" + sk + "' has several outputs (" + ", ".join(p.name for p in src.defn.outputs) + "); name one")
            sp = src.defn.outputs[0].name
        if dp is None:
            if len(dst.defn.inputs) != 1:
                raise SpecError(where + ": '" + dk + "' has several inputs (" + ", ".join(p.name for p in dst.defn.inputs) + "); name one")
            dp = dst.defn.inputs[0].name
        sport, dport = src.out_port(sp), dst.in_port(dp)
        if sport is None:
            raise SpecError(where + ": '" + sk + "' (" + src.kind_name + ") has no output '" + sp + "' (outputs: " + ", ".join(p.name for p in src.defn.outputs) + ")")
        if dport is None:
            raise SpecError(where + ": '" + dk + "' (" + dst.kind_name + ") has no input '" + dp + "' (inputs: " + ", ".join(p.name for p in dst.defn.inputs) + ")")
        if src is dst:
            raise SpecError(where + ": a node cannot feed itself")
        sig = (sk, sp, dk, dp)
        if sig in seen:
            raise SpecError(where + ": this wire is listed twice")
        seen.add(sig)
        if dp in dst.values:
            raise SpecError(where + ": input '" + dp + "' of '" + dk + "' has a typed value as well as a wire; keep one")
        existing = dst.wired_in.setdefault(dp, [])
        if existing and not dport.multi:
            raise SpecError(where + ": input '" + dp + "' of '" + dk + "' takes one wire (it is already fed by '" + existing[0][0].key + "')")
        existing.append((src, sp))
        w = _compat_warning(src, sport, dst, dport)
        if w:
            warnings.append(w)
        wires.append(Wire(src, sp, dst, dp))
    # required inputs
    for n in nodes:
        for p in n.defn.inputs:
            if p.name in n.wired_in or p.name in n.values or p.has_default:
                continue
            if n.defn.interactive and n.kind_name == "List.Create":
                raise SpecError("node '" + n.key + "' (List.Create): item '" + p.name + "' is not wired; lower 'count'")
            hint = (
                "type a value under 'values'" if p.editor not in ("none", "model")
                else "wire a node into it"
            )
            raise SpecError("node '" + n.key + "' (" + n.kind_name + "): required input '" + p.name + "' is neither wired nor given a value - " + hint)
        # a typed value on a port that is hidden by default (document) is pointless but legal; nothing to do
    # cycles
    colour: dict[str, int] = {}
    succ: dict[str, list[str]] = {n.key: [] for n in nodes}
    for w in wires:
        succ[w.src.key].append(w.dst.key)

    def visit(k: str, trail: list[str]) -> None:
        colour[k] = 1
        for nxt in succ[k]:
            if colour.get(nxt) == 1:
                raise SpecError("the wires form a loop: " + " -> ".join(trail + [k, nxt]))
            if colour.get(nxt) is None:
                visit(nxt, trail + [k])
        colour[k] = 2

    for n in nodes:
        if colour.get(n.key) is None:
            visit(n.key, [])
    # every node should be connected to something, otherwise the layout and the picture are misleading
    connected = {w.src.key for w in wires} | {w.dst.key for w in wires}
    for n in nodes:
        if n.key not in connected and len(nodes) > 1:
            raise SpecError("node '" + n.key + "' has no wires; wire it in or remove it")
    # List.Create: items must be contiguous from item0
    for n in nodes:
        if n.kind_name == "List.Create":
            wired = [p.name for p in n.defn.inputs if p.name in n.wired_in]
            if len(wired) != len(n.defn.inputs):
                raise SpecError("node '" + n.key + "' (List.Create) has " + str(len(n.defn.inputs)) + " items but " + str(len(wired)) + " are wired")
    return nodes, wires, warnings


# ===================================================================== layout

def node_geometry(n: BNode) -> dict:
    """Estimated size of the node on the canvas and the height of each of its sockets (pixels)."""
    n_out = len(n.defn.outputs)
    body = BODY_HEIGHT.get(n.kind_name, 0)
    visible: list[str] = []
    hidden = 0
    for p in n.defn.inputs:
        shown = p.name in n.wired_in or p.name in n.values or not (p.has_default and p.family == "document")
        if shown:
            visible.append(p.name)
        else:
            hidden += 1
    top_of_inputs = HEADER_H + OUT_ROW_H * n_out + body
    height = top_of_inputs + ROW_H * len(visible) + (CHIP_H if hidden else 0) + PAD_H
    return {
        "w": WIDE_W if n.kind_name in WIDE_KINDS else NODE_W,
        "h": height,
        "out_y": {p.name: HEADER_H + OUT_ROW_H * i + OUT_ROW_H / 2 for i, p in enumerate(n.defn.outputs)},
        "in_y": {name: top_of_inputs + ROW_H * i + ROW_H / 2 for i, name in enumerate(visible)},
    }


def _isotonic(desired: list[float], heights: list[float], gap: float) -> list[float]:
    """Positions closest to `desired` (least squares) that keep every node below the previous one plus the gap."""
    n = len(desired)
    offsets = [0.0] * n
    for i in range(1, n):
        offsets[i] = offsets[i - 1] + heights[i - 1] + gap
    target = [desired[i] - offsets[i] for i in range(n)]
    blocks: list[list[float]] = []  # [sum, count]
    for t in target:
        blocks.append([t, 1])
        while len(blocks) > 1 and blocks[-2][0] / blocks[-2][1] > blocks[-1][0] / blocks[-1][1]:
            s, c = blocks.pop()
            blocks[-1][0] += s
            blocks[-1][1] += c
    z: list[float] = []
    for s, c in blocks:
        z.extend([s / c] * int(c))
    return [z[i] + offsets[i] for i in range(n)]


def layout(nodes: list[BNode], wires: list[Wire]) -> dict[str, dict]:
    """Give every node an x, y and column. Left to right by depth; each column stacked to keep wires level."""
    geo = {n.key: node_geometry(n) for n in nodes}
    preds: dict[str, list[Wire]] = {n.key: [] for n in nodes}
    succs: dict[str, list[Wire]] = {n.key: [] for n in nodes}
    for w in wires:
        preds[w.dst.key].append(w)
        succs[w.src.key].append(w)
    # depth = one column right of the deepest node that feeds it
    depth: dict[str, int] = {}
    order = list(nodes)

    def d(n: BNode) -> int:
        if n.key not in depth:
            depth[n.key] = 0 if not preds[n.key] else 1 + max(d(w.src) for w in preds[n.key])
        return depth[n.key]

    for n in order:
        d(n)
    # a node with slack sits next to what it feeds (so input nodes stand beside the node that takes them)
    for n in sorted(order, key=lambda m: -depth[m.key]):
        if succs[n.key]:
            depth[n.key] = min(depth[w.dst.key] for w in succs[n.key]) - 1
    shift = -min(depth.values())
    for k in depth:
        depth[k] += shift
    max_col = max(depth.values())
    columns: list[list[BNode]] = [[] for _ in range(max_col + 1)]
    for n in order:
        n.column = depth[n.key]
        columns[n.column].append(n)
    # x per column
    col_x: list[float] = []
    x = 0.0
    for col in columns:
        col_x.append(x)
        x += (max(geo[n.key]["w"] for n in col) if col else NODE_W) + COLUMN_GAP
    for n in nodes:
        n.x = col_x[n.column]
    # initial stack
    for col in columns:
        y = 0.0
        for n in col:
            n.y = y
            y += geo[n.key]["h"] + ROW_GAP

    def desired_from_preds(n: BNode) -> Optional[float]:
        ds = [w.src.y + geo[w.src.key]["out_y"][w.src_port] - geo[n.key]["in_y"][w.dst_port] for w in preds[n.key]]
        return sum(ds) / len(ds) if ds else None

    def desired_from_succs(n: BNode) -> Optional[float]:
        ds = [w.dst.y + geo[w.dst.key]["in_y"][w.dst_port] - geo[n.key]["out_y"][w.src_port] for w in succs[n.key]]
        return sum(ds) / len(ds) if ds else None

    def place(col: list[BNode], desired_fn) -> None:
        wanted = []
        for n in col:
            dv = desired_fn(n)
            wanted.append((n.y if dv is None else dv, n.index, n))
        wanted.sort(key=lambda t: (t[0], t[1]))
        ys = _isotonic([t[0] for t in wanted], [geo[t[2].key]["h"] for t in wanted], ROW_GAP)
        for (_, _, n), y in zip(wanted, ys):
            n.y = y
        col[:] = [t[2] for t in wanted]

    for _ in range(10):
        for col in columns[1:]:
            place(col, desired_from_preds)
        for col in reversed(columns[:-1]):
            place(col, desired_from_succs)
    for col in columns[1:]:
        place(col, desired_from_preds)
    # tidy: smallest y is 0, positions on a 10 px grid, no overlaps after rounding
    min_y = min(n.y for n in nodes)
    for col in columns:
        prev_bottom = None
        for n in sorted(col, key=lambda m: m.y):
            n.y = round((n.y - min_y) / 10.0) * 10.0
            if prev_bottom is not None and n.y < prev_bottom + ROW_GAP:
                n.y = math.ceil((prev_bottom + ROW_GAP) / 10.0) * 10.0
            prev_bottom = n.y + geo[n.key]["h"]
    min_y = min(n.y for n in nodes)
    for n in nodes:
        n.y -= min_y
        n.x = round(n.x / 10.0) * 10.0
    return geo


def note_height(text: str) -> float:
    chars_per_line = 44
    lines = 0
    for paragraph in text.split("\n"):
        lines += max(1, math.ceil(len(paragraph) / chars_per_line))
    return lines * 18 + 20


def frame_rect(members: list[BNode], geo: dict[str, dict]) -> tuple[float, float, float, float]:
    left = min(n.x for n in members)
    top = min(n.y for n in members)
    right = max(n.x + geo[n.key]["w"] for n in members)
    bottom = max(n.y + geo[n.key]["h"] for n in members)
    pad, header = 20.0, 42.0
    return left - pad, top - pad - header, right - left + pad * 2, bottom - top + pad * 2 + header


# ===================================================================== writing the file

def stable_id(stem: str, kind: str, *parts: str) -> str:
    return hashlib.md5(("/".join([NODE_ID_NAMESPACE, stem, kind, *parts])).encode("utf-8")).hexdigest()


def app_version() -> str:
    props = (REPO / "Directory.Build.props").read_text(encoding="utf-8")
    m = re.search(r"<Version>([^<]+)</Version>", props)
    return m.group(1).strip() if m else "0.0.0"


def _port_json(n: BNode, p: PortInfo) -> dict:
    wired = p.name in n.wired_in
    out: dict[str, Any] = {
        "Name": p.name,
        "UsingDefaultValue": bool(p.has_default and not wired),
        "Level": -1,
        "UseLevels": False,
        "KeepListStructure": False,
    }
    if p.name in n.player_inputs:
        out["Player"] = True
    if p.name in n.values:
        out["UserValue"] = n.values[p.name]
    return out


def _node_json(stem: str, n: BNode) -> dict:
    out: dict[str, Any] = {"Id": stable_id(stem, "node", n.key), "NodeType": n.defn.node_type}
    if n.defn.node_type == "ZeroTouch":
        out["DefinitionId"] = n.defn.definition_id
        out["Assembly"] = n.defn.assembly
    out["Name"] = n.title
    out["X"] = float(n.x)
    out["Y"] = float(n.y)
    out["Lacing"] = n.lacing
    out["IsFrozen"] = False
    if n.player is not None:
        out["Player"] = n.player
    out["InputPorts"] = [_port_json(n, p) for p in n.defn.inputs]
    out["OutputPorts"] = [{"Name": p.name} for p in n.defn.outputs]
    out["Data"] = n.data
    return out


def one_sentence(text: str) -> bool:
    t = text.strip()
    return bool(t) and t.endswith(".") and not re.search(r"[.!?]\s+[A-Z]", t[:-1]) and len(t) <= 220


def build_graph(spec: dict, stem: str) -> tuple[str, list[str]]:
    """Return the text of the .dyc file for a spec, and the warnings found on the way."""
    _check_keys(spec, TOP_KEYS, "spec")
    name = spec.get("name")
    description = spec.get("description")
    if not isinstance(name, str) or not name.strip():
        raise SpecError("'name' is required: the graph's name as the editor shows it")
    if not isinstance(description, str) or not one_sentence(description):
        raise SpecError("'description' must be one sentence ending with a full stop (at most 220 characters)")
    run = spec.get("run", "Manual")
    if run not in RUN_TYPES:
        raise SpecError("'run' is Manual or Automatic")
    nodes, wires, warnings = build_nodes_and_wires(spec, stem)
    geo = layout(nodes, wires)
    by_key = {n.key: n for n in nodes}

    # frames
    groups: list[dict] = []
    frames = spec.get("frames", [])
    if not isinstance(frames, list):
        raise SpecError("'frames' must be a list")
    frame_pad_top = 0.0
    for i, fr in enumerate(frames):
        if not isinstance(fr, dict):
            raise SpecError("frames[" + str(i) + "] must be an object")
        _check_keys(fr, {"title", "nodes", "colour"}, "frames[" + str(i) + "]")
        title, members, colour = fr.get("title"), fr.get("nodes"), fr.get("colour", "Blue")
        if not isinstance(title, str) or not title.strip():
            raise SpecError("frames[" + str(i) + "]: 'title' is required")
        if not isinstance(members, list) or not members or any(m not in by_key for m in members):
            raise SpecError("frames[" + str(i) + "]: 'nodes' lists node ids that exist")
        if colour not in FRAME_COLOURS:
            raise SpecError("frames[" + str(i) + "]: 'colour' is one of " + ", ".join(FRAME_COLOURS))
        member_nodes = [by_key[m] for m in members]
        x, y, w, h = frame_rect(member_nodes, geo)
        for other in nodes:
            if other in member_nodes:
                continue
            cx, cy = other.x + geo[other.key]["w"] / 2, other.y + geo[other.key]["h"] / 2
            if x <= cx <= x + w and y <= cy <= y + h:
                raise SpecError("frame '" + title + "' would also cover node '" + other.key + "'; reorder the nodes or change the frame")
        frame_pad_top = max(frame_pad_top, min(n.y for n in member_nodes) - y)
        groups.append({
            "Id": stable_id(stem, "frame", str(i)), "Title": title, "X": float(x), "Y": float(y), "Width": float(w), "Height": float(h),
            "Color": FRAME_COLOURS[colour],
        })

    # notes
    note_specs = list(spec.get("notes", [])) + [x for x in spec.pop("_notes_from_nodes", []) if x is not None]
    top = min([n.y for n in nodes] + [g["Y"] for g in groups])
    bottom = max([n.y + geo[n.key]["h"] for n in nodes] + [g["Y"] + g["Height"] for g in groups])
    notes_json: list[dict] = []
    above = 0.0
    for i, ns in enumerate(note_specs):
        if not isinstance(ns, dict):
            raise SpecError("notes[" + str(i) + "] must be an object")
        _check_keys(ns, {"text", "at"}, "notes[" + str(i) + "]")
        text = ns.get("text")
        if not isinstance(text, str) or not text.strip():
            raise SpecError("notes[" + str(i) + "]: 'text' is required")
        at = ns.get("at", "top")
        h = note_height(text)
        if at == "top":
            above += h + 30
            nx, ny = 0.0, top - above
        elif at == "bottom":
            nx, ny = 0.0, bottom + 50
            bottom += 50 + h
        elif isinstance(at, list) and len(at) == 2 and all(_is_number(v) for v in at):
            nx, ny = float(at[0]), float(at[1])
        else:
            raise SpecError("notes[" + str(i) + "]: 'at' is \"top\", \"bottom\" or [x, y]")
        notes_json.append({"Id": stable_id(stem, "note", str(i)), "Text": text, "X": nx, "Y": round(ny / 10.0) * 10.0})

    graph: dict[str, Any] = {
        "Dyncamelo": {"FormatVersion": 1, "MinReaderVersion": 1, "AppVersion": spec.get("appVersion") or app_version()},
        "Uuid": stable_id(stem, "graph"),
        "Name": name,
        "Description": description.strip(),
        "Nodes": [_node_json(stem, n) for n in nodes],
        "Connectors": [
            {
                "Id": stable_id(stem, "wire", w.src.key, w.src_port, w.dst.key, w.dst_port),
                "FromNode": stable_id(stem, "node", w.src.key), "FromPort": w.src_port,
                "ToNode": stable_id(stem, "node", w.dst.key), "ToPort": w.dst_port,
            }
            for w in wires
        ],
        "Notes": notes_json,
        "Groups": groups,
        "View": {"RunType": run, "Camera": {"X": 0.0, "Y": 0.0, "Zoom": 1.0}},
    }
    return json.dumps(graph, indent=2, ensure_ascii=False), warnings


# ===================================================================== command line

def spec_path(arg: str) -> Path:
    p = Path(arg)
    if p.suffix == ".json" and p.exists():
        return p
    q = SPEC_DIR / (arg if arg.endswith(".json") else arg + ".json")
    if q.exists():
        return q
    raise SpecError("no spec '" + arg + "' (looked for " + str(q) + ")")


def build_one(path: Path) -> tuple[str, str, list[str]]:
    try:
        spec = json.loads(path.read_text(encoding="utf-8"))
    except ValueError as exc:
        raise SpecError(path.name + ": not valid JSON: " + str(exc)) from exc
    if not isinstance(spec, dict):
        raise SpecError(path.name + ": a spec is a JSON object")
    try:
        text, warnings = build_graph(spec, path.stem)
    except SpecError as exc:
        raise SpecError(path.name + ": " + str(exc)) from exc
    return path.stem, text, warnings


def _normalise_for_compare(text: str) -> str:
    text = text.replace("\r\n", "\n")
    return re.sub(r'"AppVersion": "[^"]*"', '"AppVersion": "*"', text)


def main(argv: Optional[list[str]] = None) -> int:
    ap = argparse.ArgumentParser(description="Build wiki .dyc graphs from specs (see the module docstring).")
    ap.add_argument("graphs", nargs="*", help="spec names or paths; default: every spec in docs/wiki-src/graphs/specs/")
    ap.add_argument("--check", action="store_true", help="fail when a committed .dyc is not what its spec builds")
    ap.add_argument("--node", metavar="NAME", help="describe a node (id, ports, editors, defaults, choices) and exit")
    ap.add_argument("--stdout", action="store_true", help="print the built graph instead of writing it")
    args = ap.parse_args(argv)

    if args.node:
        try:
            print(node_summary(args.node))
        except SpecError as exc:
            print("error: " + str(exc), file=sys.stderr)
            return 1
        return 0

    try:
        specs = [spec_path(g) for g in args.graphs] if args.graphs else sorted(SPEC_DIR.glob("*.json"))
    except SpecError as exc:
        print("error: " + str(exc), file=sys.stderr)
        return 1
    if not specs:
        print("error: no specs found in " + str(SPEC_DIR), file=sys.stderr)
        return 1

    failed = False
    for path in specs:
        try:
            stem, text, warnings = build_one(path)
        except SpecError as exc:
            print("error: " + str(exc), file=sys.stderr)
            failed = True
            continue
        for w in warnings:
            print("warning: " + path.name + ": " + w, file=sys.stderr)
        target = GRAPH_DIR / (stem + ".dyc")
        if args.stdout:
            print(text)
        elif args.check:
            if not target.exists():
                print("error: " + target.name + " is missing; run python3 tools/wiki_graph.py " + stem, file=sys.stderr)
                failed = True
                continue
            committed = target.read_text(encoding="utf-8")
            if _normalise_for_compare(committed) != _normalise_for_compare(text):
                diff = difflib.unified_diff(
                    _normalise_for_compare(committed).splitlines(), _normalise_for_compare(text).splitlines(),
                    "committed " + target.name, "built from " + path.name, lineterm="", n=1)
                print("error: " + target.name + " is not what " + path.name + " builds:", file=sys.stderr)
                for line in list(diff)[:30]:
                    print("  " + line, file=sys.stderr)
                print("  run: python3 tools/wiki_graph.py " + stem, file=sys.stderr)
                failed = True
            else:
                print("ok: " + target.name)
        else:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(text, encoding="utf-8", newline="\n")
            print("wrote " + str(target.relative_to(REPO)))
    if args.check and not args.graphs:
        stems = {p.stem for p in specs}
        for dyc in sorted(GRAPH_DIR.glob("*.dyc")):
            if dyc.stem not in stems:
                print("error: " + dyc.name + " has no spec in " + str(SPEC_DIR.relative_to(REPO)), file=sys.stderr)
                failed = True
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
