#!/usr/bin/env python3
"""Generate the Dyncamelo node catalog (dyncamelo-nodes.json) from source.

Walks the zero-touch node sources (src/Dyncamelo.Nodes, src/Dyncamelo.Navisworks)
and the interactive NodeModel nodes (src/Dyncamelo.Core/Nodes plus the three in
Dyncamelo.Nodes), mirroring the import rules in
src/Dyncamelo.Core/Loader/AssemblyNodeLoader.cs:

  * every public class contributes its public static methods
  * name    = [NodeName] or "Class.Method"
  * category= method [NodeCategory] ?? class [NodeCategory] ?? namespace-derived
  * inputs  = method parameters (with defaults and <param> docs)
  * outputs = [MultiReturn] keys, or the single return port named by
              [return: NodeName], or "result"; void methods pass input 0 through
  * methods with `params`, by-ref or delegate parameters and
    [IsVisibleInLibrary(false)] / [TypeConverterRegistration] members are skipped

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
INTERACTIVE_FILES = [
    REPO / "src" / "Dyncamelo.Nodes" / "WatchListNode.cs",
    REPO / "src" / "Dyncamelo.Nodes" / "ColorPickerNode.cs",
    REPO / "src" / "Dyncamelo.Nodes" / "ListCreateNode.cs",
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
            node = parse_method(signature, "\n".join(attr_lines), doc_lines, cls, ns, assembly)
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
    signature: str, attr_block: str, doc_lines: list[str], cls: dict, ns: str, assembly: str
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

    docs = parse_xml_docs(doc_lines)
    inputs = []
    for p in split_top_level(params_text):
        p = re.sub(r"^\[[^\]]*\]\s*", "", p)  # parameter attributes
        default = None
        if "=" in p:
            p, default = (x.strip() for x in p.split("=", 1))
        tokens = p.rsplit(" ", 1)
        if len(tokens) != 2:
            continue
        ptype, pname = tokens
        entry = {
            "name": pname,
            "type": friendly_type(ptype),
            "description": docs["params"].get(pname, ""),
        }
        if default is not None:
            entry["default"] = friendly_default(default)
        inputs.append(entry)

    multi = attr_strings(attr_block, "MultiReturn")
    if multi and "Dictionary" in return_type:
        outputs = [{"name": k, "type": "any", "description": ""} for k in multi]
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

    return {
        "name": node_name,
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
        # word boundary so "Name" never matches the "TypeName" const
        m = re.search(r"(?<![A-Za-z])" + prop + r'\s*=\s*"((?:[^"\\]|\\.)*)"\s*;', text)
        return m.group(1) if m else ""

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


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=str(REPO / "docs" / "dyncamelo-nodes.json"))
    ap.add_argument("--baseline", help="existing catalog to diff node names against")
    args = ap.parse_args()

    nodes: list[dict] = []
    for f in iter_source_files(ZERO_TOUCH_DIRS):
        parse_zero_touch_file(f, nodes)
    for f in iter_source_files(INTERACTIVE_DIRS) + INTERACTIVE_FILES:
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

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(catalog, indent=1, ensure_ascii=False) + "\n", encoding="utf-8")
    empty_desc = sum(1 for n in nodes if not n["description"])
    print(
        f"wrote {out} — {len(nodes)} nodes, {len(categories)} categories, "
        f"{empty_desc} without descriptions"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
