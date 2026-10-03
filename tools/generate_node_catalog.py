#!/usr/bin/env python3
"""Generate the Dyncamelo node catalog (dyncamelo-nodes.json) from source.

Walks the zero-touch node sources (src/Dyncamelo.Nodes, src/Dyncamelo.Navisworks)
and the interactive NodeModel nodes (every file under those directories and
src/Dyncamelo.Core/Nodes that defines a NodeModel subclass), mirroring the import rules in
src/Dyncamelo.Core/Loader/AssemblyNodeLoader.cs:

  * every public class contributes its public static methods
  * name    = [NodeName] or "Class.Method"
  * category= method [NodeCategory] ?? class [NodeCategory] ?? namespace-derived
  * inputs  = method parameters (with defaults and <param> docs)
  * outputs = [MultiReturn] keys, or the single return port named by
              [return: NodeName], or "result"; void methods pass input 0 through
  * methods with `params`, by-ref or delegate parameters and
    [IsVisibleInLibrary(false)] / [NodeDeprecated] / [TypeConverterRegistration] members are skipped
  * id      = the identity a saved graph (.dyc) uses for the node: for a zero-touch node the definition id
              AssemblyNodeLoader.GetFunctionSignature builds (`Namespace.Class.Method@type1,type2`, "DefinitionId" in
              the file, with `assembly` as "Assembly"), for an interactive node its serialized type tag ("NodeType")

The output feeds the node-library browser on bimcamel.com/plugins/dyncamelo, so
types are prettified (IEnumerable<ModelItem> -> "ModelItem[]", double ->
"number") and every port carries its XML-doc description.

Usage:
    python3 tools/generate_node_catalog.py [--out FILE] [--baseline OLD.json]

--baseline compares the generated node-name set against an existing catalog and
fails on unexpected disappearances (guards against parser regressions).
"""

from __future__ import annotations

import argparse
import html
import json
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent

ZERO_TOUCH_DIRS = [
    REPO / "src" / "Dyncamelo.Nodes",
    REPO / "src" / "Dyncamelo.Navisworks",
]
INTERACTIVE_DIRS = [
    REPO / "src" / "Dyncamelo.Core" / "Nodes",
]
EXCLUDED_PARTS = {"Internal", "bin", "obj"}

# ---------------------------------------------------------------- C# helpers


def parse_cs_string(text: str, i: int) -> tuple[str, int]:
    """Parse the C# string literal starting at text[i] (a quote); returns
    (value, index-after-closing-quote). Handles \\-escapes; verbatim strings
    (@"...") are handled by the caller."""
    assert text[i] == '"'
    out = []
    i += 1
    while i < len(text):
        c = text[i]
        if c == "\\":
            nxt = text[i + 1]
            mapping = {"n": "\n", "t": "\t", "r": "\r", '"': '"', "\\": "\\", "'": "'", "0": "\0"}
            out.append(mapping.get(nxt, nxt))
            i += 2
            continue
        if c == '"':
            return "".join(out), i + 1
        out.append(c)
        i += 1
    raise ValueError("unterminated string literal")


def split_top_level(text: str, sep: str = ",") -> list[str]:
    """Split text on separators that sit outside (), [], <> and strings."""
    parts, depth, buf, i = [], 0, [], 0
    while i < len(text):
        c = text[i]
        if c == '"':
            start = i
            _, i = parse_cs_string(text, i)
            buf.append(text[start:i])
            continue
        if c in "([<":
            depth += 1
        elif c in ")]>":
            depth -= 1
        if c == sep and depth == 0:
            parts.append("".join(buf))
            buf = []
        else:
            buf.append(c)
        i += 1
    if buf:
        parts.append("".join(buf))
    return [p.strip() for p in parts if p.strip()]


def eval_string_expr(expr: str) -> str:
    """Evaluate a C# constant string expression: literals concatenated with +."""
    out, i = [], 0
    while i < len(expr):
        c = expr[i]
        if c == '"':
            value, i = parse_cs_string(expr, i)
            out.append(value)
        else:
            i += 1
    return "".join(out)


def find_attr_args(block: str, attr: str) -> str | None:
    """Return the raw argument text of [attr(...)] within an attribute block."""
    m = re.search(r"\b" + attr + r"\s*\(", block)
    if not m:
        # bare [attr] with no args
        return "" if re.search(r"\b" + attr + r"\b", block) else None
    i = m.end()
    depth, start = 1, i
    while i < len(block) and depth:
        c = block[i]
        if c == '"':
            _, i = parse_cs_string(block, i)
            continue
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
        i += 1
    return block[start : i - 1]


def attr_strings(block: str, attr: str) -> list[str] | None:
    args = find_attr_args(block, attr)
    if args is None:
        return None
    return [eval_string_expr(a) for a in split_top_level(args)]


# ------------------------------------------------------------ XML-doc helpers


def clean_doc(text: str) -> str:
    """Flatten XML-doc markup to plain text."""
    text = re.sub(r'<see\s+cref="[^"]*?([A-Za-z0-9_]+)(?:\([^)]*\))?"\s*/>', r"\1", text)
    text = re.sub(r'<see\s+cref="[^"]*?([A-Za-z0-9_]+)(?:\([^)]*\))?"\s*>(.*?)</see>', r"\2", text)
    text = re.sub(r'<paramref\s+name="([^"]+)"\s*/>', r"\1", text)
    text = re.sub(r"<c>(.*?)</c>", r"\1", text, flags=re.S)
    text = re.sub(r"<[^>]+>", "", text)
    text = html.unescape(text)
    return re.sub(r"\s+", " ", text).strip()


def parse_xml_docs(doc_lines: list[str]) -> dict:
    doc = "\n".join(line.strip().lstrip("/").strip() for line in doc_lines)
    params = {
        m.group(1): clean_doc(m.group(2))
        for m in re.finditer(r'<param\s+name="([^"]+)"\s*>(.*?)</param>', doc, re.S)
    }
    returns = re.search(r"<returns>(.*?)</returns>", doc, re.S)
    summary = re.search(r"<summary>(.*?)</summary>", doc, re.S)
    return {
        "params": params,
        "returns": clean_doc(returns.group(1)) if returns else "",
        "summary": clean_doc(summary.group(1)) if summary else "",
    }


# ------------------------------------------------------------- type printing

LIST_LIKE = (
    "IEnumerable",
    "IList",
    "List",
    "IReadOnlyList",
    "ICollection",
    "IReadOnlyCollection",
)
PRIMITIVES = {
    "double": "number",
    "float": "number",
    "decimal": "number",
    "int": "integer",
    "long": "integer",
    "short": "integer",
    "uint": "integer",
    "ulong": "integer",
    "bool": "boolean",
    "string": "string",
    "object": "any",
    "void": "nothing",
    "DateTime": "datetime",
    "TimeSpan": "duration",
    "Guid": "guid",
}
DYNCAMELO_TYPES = {
    "DyncameloPoint": "Point",
    "DyncameloVector": "Vector",
    "DyncameloColor": "Color",
    "DyncameloBoundingBox": "BoundingBox",
}


# The sockets of a [MultiReturn] node take their colour from [PortKinds]; the catalogue names the same thing in the
# vocabulary of the other port types (PortKinds.FromTypeName reads it back).
KIND_TYPES = {
    "number": "number",
    "integer": "integer",
    "boolean": "boolean",
    "text": "string",
    "datetime": "datetime",
    "colour": "Color",
    "geometry": "Point",
    "item": "ModelItem",
    "selection": "ModelItemCollection",
    "viewpoint": "Viewpoint",
    "clash": "ClashResult",
    "document": "Document",
    "data": "dict",
    "file": "file",
    "action": "IWorkflowAction",
}


def kind_type(kind: str) -> str:
    stars = len(kind) - len(kind.rstrip("*"))
    base = KIND_TYPES.get(kind.rstrip("*").strip().lower(), "any")
    return base + "[]" * stars if base != "any" else "any"


def friendly_type(cs: str) -> str:
    t = re.sub(r"\s+", " ", cs).strip()
    for prefix in ("params ", "this "):
        if t.startswith(prefix):
            t = t[len(prefix) :]
    if t.endswith("?"):
        t = t[:-1]
    if t.endswith("[]"):
        return friendly_type(t[:-2]) + "[]"
    m = re.match(r"^([A-Za-z_][A-Za-z0-9_.]*)<(.+)>$", t)
    if m:
        outer, inner = m.group(1).split(".")[-1], m.group(2)
        if outer in LIST_LIKE:
            return friendly_type(inner) + "[]"
        if outer.endswith("Dictionary"):
            return "dict"
        if outer == "Nullable":
            return friendly_type(inner)
        return outer
    t = t.split(".")[-1]
    if t in PRIMITIVES:
        return PRIMITIVES[t]
    return DYNCAMELO_TYPES.get(t, t)


def friendly_default(expr: str) -> str:
    e = expr.strip()
    if e == "null":
        return "null"
    if e in ("default", "default!"):
        return "default"
    if e.startswith('"'):
        return '"' + eval_string_expr(e) + '"'
    e = re.sub(r"\b(double|float|int|long)\.", "", e)
    return re.sub(r"[dDfFmM]$", "", e) if re.match(r"^-?[\d.]+[dDfFmM]$", e) else e


# ------------------------------------------------------------ definition ids

# Parameter types as the loader writes them into an id (AssemblyNodeLoader.GetMangledTypeName): C# keywords for the
# built-in types, the full name for everything else, `Name<arg,arg>` for generics (no spaces), `T[]` and `T?`.
KEYWORD_TYPES = {
    "bool", "byte", "sbyte", "char", "short", "ushort", "int", "uint", "long", "ulong",
    "float", "double", "decimal", "string", "object",
}
SYSTEM_TYPES = {
    "DateTime": "System.DateTime",
    "DateTimeOffset": "System.DateTimeOffset",
    "TimeSpan": "System.TimeSpan",
    "Guid": "System.Guid",
    "IEnumerable": "System.Collections.Generic.IEnumerable",
    "ICollection": "System.Collections.Generic.ICollection",
    "IList": "System.Collections.Generic.IList",
    "List": "System.Collections.Generic.List",
    "IReadOnlyList": "System.Collections.Generic.IReadOnlyList",
    "IReadOnlyCollection": "System.Collections.Generic.IReadOnlyCollection",
    "Dictionary": "System.Collections.Generic.Dictionary",
}
SYSTEM_VALUE_TYPES = {"DateTime", "DateTimeOffset", "TimeSpan", "Guid"}
# Types that node signatures take from the host application or a vendored library rather than from this repository.
# Unknown type names fail the generator (see full_type_name), so a new one has to be added here on purpose.
EXTERNAL_TYPES = {
    "ClashResult": "Autodesk.Navisworks.Api.Clash.ClashResult",
    "ClashResultGroup": "Autodesk.Navisworks.Api.Clash.ClashResultGroup",
    "ClashTest": "Autodesk.Navisworks.Api.Clash.ClashTest",
    "DataProperty": "Autodesk.Navisworks.Api.DataProperty",
    "Document": "Autodesk.Navisworks.Api.Document",
    "FolderItem": "Autodesk.Navisworks.Api.FolderItem",
    "Model": "Autodesk.Navisworks.Api.Model",
    "ModelItem": "Autodesk.Navisworks.Api.ModelItem",
    "SavedItem": "Autodesk.Navisworks.Api.SavedItem",
    "SavedViewpoint": "Autodesk.Navisworks.Api.SavedViewpoint",
    "SelectionSet": "Autodesk.Navisworks.Api.SelectionSet",
    "TimelinerTask": "Autodesk.Navisworks.Api.Timeliner.TimelinerTask",
    "Units": "Autodesk.Navisworks.Api.Units",
    "CoordOptions": "BIMCamel.Ifc.CoordOptions",
    "ParamMapRule": "BIMCamel.Data.ParamMapRule",
    "PropertyRoles": "BIMCamel.Data.PropertyRoles",
    "SpatialNames": "BIMCamel.Ifc.SpatialNames",
}
EXTERNAL_VALUE_TYPES = {"Units"}  # an enum

TYPE_DECL_RE = re.compile(
    r"^\s*public\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+)*(class|struct|enum|interface|record)\s+([A-Za-z_][A-Za-z0-9_]*)"
)
NAMESPACE_RE = re.compile(r"^\s*namespace\s+([\w.]+)", re.M)
USING_RE = re.compile(r"^\s*using\s+([\w.]+)\s*;", re.M)

_repo_types: dict | None = None


def repo_types() -> dict:
    """Public types declared in this repository's node sources: simple name -> [(namespace, is_value_type)]."""
    global _repo_types
    if _repo_types is None:
        found: dict = {}
        for d in (REPO / "src" / "Dyncamelo.Core", *ZERO_TOUCH_DIRS):
            for f in sorted(d.rglob("*.cs")):
                if EXCLUDED_PARTS.intersection(f.relative_to(d).parts):
                    continue
                text = f.read_text(encoding="utf-8-sig")
                ns = NAMESPACE_RE.search(text)
                if not ns:
                    continue
                for line in text.splitlines():
                    m = TYPE_DECL_RE.match(line)
                    if m:
                        found.setdefault(m.group(2), []).append((ns.group(1), m.group(1) in ("struct", "enum")))
        _repo_types = found
    return _repo_types


class IdContext:
    """The namespace and `using` directives of the file a method is declared in (they decide what a type name means)."""

    def __init__(self, ns: str, usings: set[str], where: str):
        self.ns, self.usings, self.where = ns, usings, where


def full_type_name(name: str, ctx: IdContext) -> tuple[str, bool]:
    """(full name, is a value type) of a simple type name as it appears in a signature of the file in ctx."""
    if name in KEYWORD_TYPES:
        return name, name not in ("string", "object")
    if "." in name:  # already qualified in the source
        name = name.rsplit(".", 1)[1]
    if name in SYSTEM_TYPES:
        return SYSTEM_TYPES[name], name in SYSTEM_VALUE_TYPES
    if name in EXTERNAL_TYPES:
        return EXTERNAL_TYPES[name], name in EXTERNAL_VALUE_TYPES
    candidates = repo_types().get(name, [])
    if len(candidates) > 1:
        scoped = [c for c in candidates if c[0] == ctx.ns or c[0] in ctx.usings]
        candidates = scoped or candidates
    if len({c[0] for c in candidates}) == 1:
        return candidates[0][0] + "." + name, candidates[0][1]
    raise ValueError(
        f"{ctx.where}: cannot work out the full name of the type '{name}' for the node id "
        f"({'ambiguous' if candidates else 'unknown'}); add it to EXTERNAL_TYPES in tools/generate_node_catalog.py"
    )


def mangle_type(cs: str, ctx: IdContext) -> str:
    """The text AssemblyNodeLoader.GetMangledTypeName gives the C# type `cs`."""
    t = re.sub(r"\s+", "", cs)
    value_nullable = False
    if t.endswith("?"):
        t = t[:-1]
        value_nullable = True  # only kept when it turns out to be Nullable<T>, a reference type's `?` is just an annotation
    if t.endswith("[]"):
        return mangle_type(t[:-2], ctx) + "[]"
    m = re.match(r"^([A-Za-z_][A-Za-z0-9_.]*)<(.+)>$", t)
    if m:
        outer = m.group(1).rsplit(".", 1)[-1]
        if outer == "Nullable":
            return mangle_type(m.group(2), ctx) + "?"
        if outer == "IDictionary" or outer == "Dictionary" or outer in SYSTEM_TYPES:
            base = ("System.Collections.Generic." + outer) if outer.endswith("Dictionary") else SYSTEM_TYPES[outer]
        else:
            base = full_type_name(outer, ctx)[0]
        args = ",".join(mangle_type(a, ctx) for a in split_top_level(m.group(2)))
        return f"{base}<{args}>"
    if t == "IDictionary":
        return "System.Collections.IDictionary"
    full, is_value = full_type_name(t, ctx)
    return full + ("?" if value_nullable and is_value else "")


# ----------------------------------------------------------- source scanning


def iter_source_files(dirs: list[Path]) -> list[Path]:
    files = []
    for d in dirs:
        for f in sorted(d.rglob("*.cs")):
            if not EXCLUDED_PARTS.intersection(f.relative_to(d).parts):
                files.append(f)
    return files


CLASS_RE = re.compile(
    r"^\s*public\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+)*(?:class|struct)\s+([A-Za-z_][A-Za-z0-9_]*)"
)
METHOD_RE = re.compile(
    r"^\s*public\s+static\s+([^=;]+?)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\($"
)


def parse_zero_touch_file(path: Path, nodes: list[dict]) -> None:
    lines = path.read_text(encoding="utf-8").splitlines()
    ns = next(
        (m.group(1) for line in lines if (m := re.match(r"\s*namespace\s+([\w.]+)", line))),
        "",
    )
    assembly = "Dyncamelo.Navisworks" if "Dyncamelo.Navisworks" in str(path) else "Dyncamelo.Nodes"
    usings = {m.group(1) for m in USING_RE.finditer("\n".join(lines))}

    doc_lines: list[str] = []
    attr_lines: list[str] = []
    cls: dict | None = None

    i = 0
    while i < len(lines):
        line = lines[i]
        stripped = line.strip()

        if stripped.startswith("///"):
            doc_lines.append(stripped)
            i += 1
            continue
        if stripped.startswith("["):
            # accumulate (possibly multi-line) attribute
            buf = [line]
            while not balanced_brackets("\n".join(buf)):
                i += 1
                buf.append(lines[i])
            attr_lines.append("\n".join(buf))
            i += 1
            continue

        cm = CLASS_RE.match(line)
        if cm:
            block = "\n".join(attr_lines)
            vis_args = find_attr_args(block, "IsVisibleInLibrary")
            hidden = vis_args is not None and "false" in vis_args
            cls = {
                "name": cm.group(1),
                "category": (attr_strings(block, "NodeCategory") or [None])[0],
                "description": (attr_strings(block, "NodeDescription") or [None])[0],
                "hidden": hidden,
            }
            doc_lines, attr_lines = [], []
            i += 1
            continue

        # public static method signature (may span lines up to the closing paren)
        sig_match = re.match(r"^\s*public\s+static\s+", line)
        if sig_match and cls and not cls["hidden"]:
            buf = [line]
            while not balanced_parens("\n".join(buf)):
                i += 1
                buf.append(lines[i])
            signature = re.sub(r"\s+", " ", " ".join(s.strip() for s in buf))
            node = parse_method(signature, "\n".join(attr_lines), doc_lines, cls, ns, assembly, usings, path)
            if node:
                nodes.append(node)
            doc_lines, attr_lines = [], []
            i += 1
            continue

        if stripped and not stripped.startswith("//"):
            doc_lines, attr_lines = [], []
        i += 1


def balanced_parens(text: str) -> bool:
    return _balanced(text, "(", ")")


def balanced_brackets(text: str) -> bool:
    return _balanced(text, "[", "]")


def _balanced(text: str, open_c: str, close_c: str) -> bool:
    depth, i, seen = 0, 0, False
    while i < len(text):
        c = text[i]
        if c == '"':
            _, i = parse_cs_string(text, i)
            continue
        if c == open_c:
            depth += 1
            seen = True
        elif c == close_c:
            depth -= 1
        i += 1
    return seen and depth == 0


def parse_method(
    signature: str, attr_block: str, doc_lines: list[str], cls: dict, ns: str, assembly: str,
    usings: set[str], path: Path,
) -> dict | None:
    # signature: "public static <return type> <Name>(<params>) ..."
    m = re.match(r"public\s+static\s+(.*?)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", signature)
    if not m:
        return None
    return_type, name = m.group(1).strip(), m.group(2)
    if "operator" in return_type.split() or return_type in ("class", "struct", "event"):
        return None
    # fields/properties, e.g. "public static readonly X Instance = new X();"
    if "=" in return_type or "readonly" in return_type.split() or "new" in return_type.split():
        return None
    if re.search(r"<", name):  # generic method definition
        return None
    if find_attr_args(attr_block, "TypeConverterRegistration") is not None:
        return None
    vis = find_attr_args(attr_block, "IsVisibleInLibrary")
    if vis is not None and "false" in vis:
        return None
    # A retired node still loads in old graphs but is not offered, so it is not in the catalogue either.
    if find_attr_args(attr_block, "NodeDeprecated") is not None:
        return None

    # parameter text: between the first '(' after the name and its match
    open_idx = signature.index("(", m.end() - 1)
    depth, j = 0, open_idx
    while j < len(signature):
        c = signature[j]
        if c == '"':
            _, j = parse_cs_string(signature, j)
            continue
        if c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                break
        j += 1
    params_text = signature[open_idx + 1 : j]
    if "params " in params_text:  # loader rejects params-array methods
        return None
    # ...and by-ref parameters and delegate-typed ones, which have no node ports.
    for raw in split_top_level(params_text):
        bare = re.sub(r"^(?:\[[^\]]*\]\s*)+", "", raw).split("=", 1)[0].strip()
        if re.match(r"^(?:ref|out|in)\s", bare) or re.match(r"^(?:Func|Action|Predicate|Comparison|Converter)\b", bare):
            return None

    docs = parse_xml_docs(doc_lines)
    inputs = []
    id_types: list[str] = []
    for p in split_top_level(params_text):
        multi_input = "[MultiInput]" in p
        p = re.sub(r"^(?:\[[^\]]*\]\s*)+", "", p)  # parameter attributes
        default = None
        if "=" in p:
            p, default = (x.strip() for x in p.split("=", 1))
        tokens = p.rsplit(" ", 1)
        if len(tokens) != 2:
            continue
        ptype, pname = tokens
        id_types.append(ptype)
        entry = {
            "name": pname,
            "type": friendly_type(ptype),
            "description": docs["params"].get(pname, ""),
        }
        if default is not None:
            entry["default"] = friendly_default(default)
        if multi_input:
            entry["multiInput"] = True
        inputs.append(entry)

    multi = attr_strings(attr_block, "MultiReturn")
    if multi and "Dictionary" in return_type:
        kinds = attr_strings(attr_block, "PortKinds") or []
        if len(kinds) != len(multi):
            kinds = [""] * len(multi)
        outputs = [{"name": k, "type": kind_type(kind), "description": ""} for k, kind in zip(multi, kinds)]
    elif return_type == "void":
        pass_type = inputs[0]["type"] if inputs else "any"
        outputs = [
            {
                "name": "result",
                "type": pass_type,
                "description": "The first input, passed through (for chaining writes in order).",
            }
        ]
    else:
        ret_attr = re.search(r"\[\s*return\s*:\s*NodeName\s*\(", attr_block)
        out_name = "result"
        if ret_attr:
            args = find_attr_args(attr_block[ret_attr.start() :], "NodeName")
            if args:
                out_name = eval_string_expr(args)
        outputs = [
            {"name": out_name, "type": friendly_type(return_type), "description": docs["returns"]}
        ]

    node_name = (attr_strings(attr_block, "NodeName") or [None])[0] or f"{cls['name']}.{name}"
    category = (attr_strings(attr_block, "NodeCategory") or [None])[0] or cls["category"]
    if not category:
        trimmed = ns[len(assembly) :].lstrip(".") if ns.startswith(assembly) else ns
        category = f"{trimmed}.{cls['name']}" if trimmed else cls["name"]
    description = (attr_strings(attr_block, "NodeDescription") or [None])[0] or cls[
        "description"
    ] or docs["summary"]

    # What a saved graph calls this node: AssemblyNodeLoader.GetFunctionSignature ("Namespace.Class.Method@type1,type2").
    ctx = IdContext(ns, usings, f"{path.relative_to(REPO).as_posix()}: {cls['name']}.{name}")
    definition_id = f"{ns}.{cls['name']}.{name}"
    if id_types:
        definition_id += "@" + ",".join(mangle_type(t, ctx) for t in id_types)

    return {
        "name": node_name,
        "id": definition_id,
        "assembly": assembly,
        "category": category,
        "description": description or "",
        "tags": attr_strings(attr_block, "NodeSearchTags") or [],
        "inputs": inputs,
        "outputs": outputs,
        "returns": docs["returns"] if multi else "",
    }


# ------------------------------------------------------- interactive NodeModels


def parse_interactive_file(path: Path, nodes: list[dict]) -> None:
    text = path.read_text(encoding="utf-8")
    if not re.search(r"class\s+\w+\s*:\s*NodeModel", text):
        return

    def const(prop: str) -> str:
        # word boundary so "Name" never matches the "TypeName" const; the value may be literals joined with +
        m = re.search(r"(?<![A-Za-z])" + prop + r'\s*=\s*("(?:[^"\\]|\\.)*"(?:\s*\+\s*"(?:[^"\\]|\\.)*")*)\s*;', text)
        return eval_string_expr(m.group(1)) if m else ""

    ports = {"inputs": [], "outputs": []}
    for m in re.finditer(r"Add(Input|Output)\s*\(", text):
        args_text = find_attr_args(text[m.start() :], "Add" + m.group(1))
        args = split_top_level(args_text or "")
        if len(args) < 2 or not args[0].startswith('"'):
            continue  # dynamic ports (List.Create) are documented separately
        type_m = re.match(r"typeof\((.+)\)", args[1])
        ports["inputs" if m.group(1) == "Input" else "outputs"].append(
            {
                "name": eval_string_expr(args[0]),
                "type": friendly_type(type_m.group(1)) if type_m else "any",
                "description": eval_string_expr(args[2]) if len(args) > 2 else "",
            }
        )

    node = {
        "name": const("Name"),
        "id": const("TypeName"),
        "category": const("Category"),
        "description": const("Description"),
        "tags": [],
        "inputs": ports["inputs"],
        "outputs": ports["outputs"],
        "returns": "",
        "interactive": True,
    }
    if node["name"] == "List.Create":
        node["inputs"] = [
            {
                "name": "item0 … itemN",
                "type": "any",
                "description": "Wire any number of items — a fresh empty input appears as you connect.",
            }
        ]
    if node["name"]:
        nodes.append(node)


NOTE_NODE = {
    "name": "Note",
    "category": "Annotation",
    "description": "A free-floating text note on the canvas — document your graph for the next person.",
    "tags": ["comment", "annotation", "text", "documentation"],
    "inputs": [],
    "outputs": [],
    "returns": "",
    "interactive": True,
}


# ------------------------------------------------------------------- driver


def first_sentence(text: str, limit: int = 170) -> str:
    """The first sentence of a description, cut to <limit> characters, safe inside a Markdown table cell."""
    text = " ".join(text.split())
    cut = len(text)
    for mark in (". ", " — ", "; "):
        i = text.find(mark)
        if 0 < i < cut:
            cut = i
    text = text[:cut].rstrip(".")
    if len(text) > limit:
        text = text[: limit - 1].rstrip() + "…"
    return text.replace("|", "\\|")


RETIRED_RE = re.compile(
    r'\[NodeName\("(?P<name>[^"]+)"\)\]\s*\[NodeDeprecated\("(?P<replacement>[^"]*)"\)\]', re.S)


def find_retired() -> list:
    """Nodes marked [NodeDeprecated]: still registered for saved graphs, not offered, not in the catalogue."""
    found = []
    for f in iter_source_files(ZERO_TOUCH_DIRS):
        for m in RETIRED_RE.finditer(f.read_text(encoding="utf-8-sig")):
            found.append((m.group("name"), m.group("replacement")))
    return sorted(found, key=lambda x: x[0].lower())


def render_markdown(catalog: dict, retired: list) -> str:
    """docs/NODE_CATALOG.md: every node by category, from the same data as the JSON."""
    lines = [
        "# Dyncamelo node catalogue",
        "",
        "> Generated from the source by `tools/generate_node_catalog.py` — do not edit by hand. "
        "Regenerate with `python3 tools/generate_node_catalog.py` after adding, renaming or retiring a node; "
        "CI fails when this file or `dyncamelo-nodes.json` is out of date.",
        "",
        f"**{catalog['count']} nodes in {len(catalog['categories'])} categories.** "
        "A `?` after an input marks it as optional. Retired nodes (still loadable in old graphs) are listed at the end.",
        "",
        "| Category | Nodes |",
        "|---|---|",
    ]
    for c in catalog["categories"]:
        anchor = c["name"].lower().replace(".", "").replace(" ", "-")
        lines.append(f"| [{c['name']}](#{anchor}) | {c['count']} |")
    lines.append("")
    by_category: dict = {}
    for n in catalog["nodes"]:
        by_category.setdefault(n["category"], []).append(n)
    for c in catalog["categories"]:
        lines += [f"## {c['name']}", "", "| Node | Inputs | Outputs | What it does |", "|---|---|---|---|"]
        for n in by_category[c["name"]]:
            ins = ", ".join(i["name"] + ("?" if "default" in i else "") for i in n["inputs"]) or "—"
            outs = ", ".join(o["name"] for o in n["outputs"]) or "—"
            kind = " *(interactive)*" if n.get("interactive") else ""
            lines.append(f"| `{n['name']}`{kind} | {ins} | {outs} | {first_sentence(n['description'])} |")
        lines.append("")
    if retired:
        lines += [
            "## Retired nodes",
            "",
            "These still load and run in saved graphs, but are no longer offered in the library. Use the replacement in new graphs.",
            "",
            "| Retired node | Use instead |",
            "|---|---|",
        ]
        lines += [f"| `{name}` | {replacement} |" for name, replacement in retired]
        lines.append("")
    return "\n".join(lines) + "\n"


def comparable(catalog: dict) -> dict:
    """The parts of a catalogue that describe the nodes (not the release it was generated at)."""
    return {k: v for k, v in catalog.items() if k != "version"}


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=str(REPO / "docs" / "dyncamelo-nodes.json"))
    ap.add_argument("--markdown", default=str(REPO / "docs" / "NODE_CATALOG.md"))
    ap.add_argument("--baseline", help="existing catalog to diff node names against")
    ap.add_argument("--check", action="store_true", help="fail when the committed JSON or Markdown is not what the source generates")
    args = ap.parse_args()

    nodes: list[dict] = []
    for f in iter_source_files(ZERO_TOUCH_DIRS):
        parse_zero_touch_file(f, nodes)
    # Every file that defines a NodeModel subclass: found by scanning, so a new interactive node cannot be forgotten.
    seen = set()
    for f in iter_source_files(INTERACTIVE_DIRS) + iter_source_files(ZERO_TOUCH_DIRS):
        if f not in seen:
            seen.add(f)
            parse_interactive_file(f, nodes)
    nodes.append(NOTE_NODE)

    names = [n["name"] for n in nodes]
    dupes = {n for n in names if names.count(n) > 1}
    if dupes:
        print(f"ERROR: duplicate node names: {sorted(dupes)}", file=sys.stderr)
        return 1

    nodes.sort(key=lambda n: (n["category"].lower(), n["name"].lower()))
    categories = {}
    for n in nodes:
        categories[n["category"]] = categories.get(n["category"], 0) + 1

    version = (REPO / "dist" / "RELEASE_VERSION").read_text().strip()
    catalog = {
        "version": version,
        "count": len(nodes),
        "categories": [{"name": c, "count": categories[c]} for c in sorted(categories)],
        "nodes": nodes,
    }

    if args.baseline:
        old = json.load(open(args.baseline))
        old_names = {n["name"] for n in old["nodes"]}
        new_names = set(names)
        missing = sorted(old_names - new_names)
        added = sorted(new_names - old_names)
        if added:
            print(f"added ({len(added)}): {added}")
        if missing:
            print(f"ERROR: baseline nodes missing ({len(missing)}): {missing}", file=sys.stderr)
            return 1

    markdown = render_markdown(catalog, find_retired())
    if args.check:
        problems = []
        try:
            committed = json.load(open(args.out, encoding="utf-8"))
        except (OSError, ValueError):
            committed = None
        if committed is None or comparable(committed) != comparable(catalog):
            problems.append(args.out)
        try:
            if Path(args.markdown).read_text(encoding="utf-8") != markdown:
                problems.append(args.markdown)
        except OSError:
            problems.append(args.markdown)
        if problems:
            print(
                "ERROR: the node catalogue is out of date: " + ", ".join(problems) +
                ". Run `python3 tools/generate_node_catalog.py` and commit the result.",
                file=sys.stderr,
            )
            return 1
        print(f"node catalogue is current — {len(nodes)} nodes, {len(categories)} categories")
        return 0

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(catalog, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    Path(args.markdown).write_text(markdown, encoding="utf-8")
    empty_desc = sum(1 for n in nodes if not n["description"])
    print(
        f"wrote {out} — {len(nodes)} nodes, {len(categories)} categories, "
        f"{empty_desc} without descriptions"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
