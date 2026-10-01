using System;

namespace Dyncamelo.Core.Loader;

/// <summary>
/// Overrides the display name of a zero-touch node (default: "Class.Method").
/// On a return value, names the single output port.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.ReturnValue, AllowMultiple = false)]
public sealed class NodeNameAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="name">Display name shown on the node header (or output port).</param>
    public NodeNameAttribute(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>The display name.</summary>
    public string Name { get; }
}

/// <summary>
/// Places a zero-touch node in the library tree. Dot-separated path, e.g. "Math.Trig".
/// Applied to a class it becomes the default for all of its methods.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class NodeCategoryAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="category">Dot-separated category path.</param>
    public NodeCategoryAttribute(string category)
    {
        Category = category ?? throw new ArgumentNullException(nameof(category));
    }

    /// <summary>The dot-separated category path.</summary>
    public string Category { get; }
}

/// <summary>
/// Description shown in the library browser and the node tooltip.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class NodeDescriptionAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="description">Human-readable description.</param>
    public NodeDescriptionAttribute(string description)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    /// <summary>The description text.</summary>
    public string Description { get; }
}

/// <summary>
/// Extra keywords the library search should match for this node.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NodeSearchTagsAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="tags">Search keywords.</param>
    public NodeSearchTagsAttribute(params string[] tags)
    {
        Tags = tags ?? Array.Empty<string>();
    }

    /// <summary>The search keywords.</summary>
    public string[] Tags { get; }
}

/// <summary>
/// Declares a node's functional role (Create / Modify / Info) explicitly,
/// overriding the loader's name-based guess. Drives the library grouping and the
/// node's canvas tint.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NodeFunctionAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="function">The node's functional role.</param>
    public NodeFunctionAttribute(Dyncamelo.Core.Graph.NodeFunction function)
    {
        Function = function;
    }

    /// <summary>The declared functional role.</summary>
    public Dyncamelo.Core.Graph.NodeFunction Function { get; }
}

/// <summary>
/// Declares a fixed set of allowed string values for a parameter, so the editor
/// can offer a dropdown instead of a free-text box (e.g. selection-resolution
/// levels, clash test types, export schemas). The values are the canonical
/// spellings shown in the list; the node's own parsing decides how leniently
/// they are matched. Purely advisory — it never changes the mangled definition
/// id, so adding it to an existing parameter keeps saved .dyc files loading.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class NodeChoicesAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="choices">The allowed values, in the order to show them.</param>
    public NodeChoicesAttribute(params string[] choices)
    {
        Choices = choices ?? Array.Empty<string>();
    }

    /// <summary>The allowed values, in display order.</summary>
    public string[] Choices { get; }
}

/// <summary>
/// Gives a <c>string</c> parameter a dropdown made of the names of an enum, optionally preceded by extra values of its own
/// (e.g. <c>"document"</c> before the unit names). The parameter stays a string, so its definition id does not change and a wired
/// text still works.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class NodeChoicesFromEnumAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="enumType">The enum whose names are offered.</param>
    /// <param name="extra">Values to offer first, before the enum's names.</param>
    public NodeChoicesFromEnumAttribute(Type enumType, params string[] extra)
    {
        EnumType = enumType ?? throw new ArgumentNullException(nameof(enumType));
        Extra = extra ?? Array.Empty<string>();
    }

    /// <summary>The enum whose names are offered.</summary>
    public Type EnumType { get; }

    /// <summary>Values offered before the enum's names.</summary>
    public string[] Extra { get; }
}

/// <summary>
/// Legacy definition ids under which a zero-touch node was previously
/// serialized. When a method's signature changes (e.g. a new optional
/// parameter is appended), its mangled definition id changes with it and
/// saved .dyc files stop resolving; listing the old id(s) here keeps those
/// files loading. Aliases only resolve when no definition owns the id
/// exactly, and re-saving writes the current id (files migrate on save).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NodeAliasesAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="aliases">Previous definition ids (full mangled signatures, e.g. "Ns.Class.Method@string,double").</param>
    public NodeAliasesAttribute(params string[] aliases)
    {
        Aliases = aliases ?? Array.Empty<string>();
    }

    /// <summary>The legacy definition ids.</summary>
    public string[] Aliases { get; }
}

/// <summary>
/// Retires a node without breaking the graphs that use it. A deprecated node is still registered — saved graphs keep loading and
/// running — but it is left out of the library and the quick search, and its description says what to use instead. Prefer
/// making the old method forward to the new one so there is a single implementation.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NodeDeprecatedAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="replacement">What to use instead, e.g. "List.Merge".</param>
    public NodeDeprecatedAttribute(string replacement)
    {
        Replacement = replacement ?? string.Empty;
    }

    /// <summary>What to use instead.</summary>
    public string Replacement { get; }
}

/// <summary>
/// An input or output of this node used to be called something else. Saved graphs store wires and typed-in values by port name, so
/// renaming a port would silently drop them; with this attribute a graph that still says <paramref name="oldName"/> finds the
/// port now called <paramref name="currentName"/>. Repeat the attribute for several ports.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class PortAliasAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="oldName">The name saved graphs may still use.</param>
    /// <param name="currentName">The port's name now (a parameter name, a [MultiReturn] key or the return name).</param>
    public PortAliasAttribute(string oldName, string currentName)
    {
        OldName = oldName ?? string.Empty;
        CurrentName = currentName ?? string.Empty;
    }

    /// <summary>The previous port name.</summary>
    public string OldName { get; }

    /// <summary>The current port name.</summary>
    public string CurrentName { get; }
}

/// <summary>
/// Marks a method returning <c>Dictionary&lt;string, object&gt;</c> as multi-output:
/// one output port per key, in the order given here. A key missing from the
/// returned dictionary yields null on that port plus a node warning.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class MultiReturnAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="keys">Dictionary keys, one per output port, in port order.</param>
    public MultiReturnAttribute(params string[] keys)
    {
        Keys = keys ?? Array.Empty<string>();
    }

    /// <summary>The output port keys, in order.</summary>
    public string[] Keys { get; }
}

/// <summary>
/// Marks a public static parameterless method that registers custom type
/// converters (via <c>Dyncamelo.Core.Types.TypeCoercion.RegisterConverter</c>)
/// for its node pack. The method is invoked once per process when the assembly
/// is registered with a <see cref="NodeRegistry"/>; it is never imported as a
/// node itself. Registration methods must be idempotent.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TypeConverterRegistrationAttribute : Attribute
{
}

/// <summary>
/// Controls whether a method (or a whole class) is imported by the zero-touch
/// loader. Use <c>[IsVisibleInLibrary(false)]</c> to hide helpers.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class IsVisibleInLibraryAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="visible">False to exclude the member from the library.</param>
    public IsVisibleInLibraryAttribute(bool visible)
    {
        Visible = visible;
    }

    /// <summary>Whether the member is imported.</summary>
    public bool Visible { get; }
}


/// <summary>
/// Soft/hard bounds and step for a numeric parameter. Drives the inline scrub
/// field: <see cref="Min"/>/<see cref="Max"/> clamp every edit, while
/// <see cref="SoftMin"/>/<see cref="SoftMax"/> only set the slider's extent
/// (typing may exceed them). Purely advisory: never changes the definition id.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class NodeRangeAttribute : Attribute
{
    /// <summary>Creates the attribute with hard bounds (soft bounds default to the same values).</summary>
    /// <param name="min">Hard minimum.</param>
    /// <param name="max">Hard maximum.</param>
    public NodeRangeAttribute(double min, double max)
    {
        Min = min;
        Max = max;
        SoftMin = min;
        SoftMax = max;
    }

    /// <summary>Hard minimum.</summary>
    public double Min { get; }

    /// <summary>Hard maximum.</summary>
    public double Max { get; }

    /// <summary>Slider extent minimum (typing may go below).</summary>
    public double SoftMin { get; set; }

    /// <summary>Slider extent maximum (typing may go above).</summary>
    public double SoftMax { get; set; }

    /// <summary>Step for arrows and scrubbing; NaN derives it from the type/default.</summary>
    public double Step { get; set; } = double.NaN;

    /// <summary>Unit suffix shown after the value ("mm", "deg", "%").</summary>
    public string Unit { get; set; } = string.Empty;
}

/// <summary>
/// Groups a parameter into a named, collapsible panel on the node ("Advanced").
/// Ports without a panel render in the main list.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class NodePanelAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="name">Panel title.</param>
    public NodePanelAttribute(string name)
    {
        Name = name ?? string.Empty;
    }

    /// <summary>Panel title.</summary>
    public string Name { get; }

    /// <summary>Whether the panel starts expanded on a newly placed node.</summary>
    public bool DefaultOpen { get; set; }
}

/// <summary>
/// Declares the semantic kind of otherwise untyped (<c>object</c>) ports so the
/// editor can colour and shape their sockets. On a method with
/// <see cref="MultiReturnAttribute"/> it lists one kind per output key, in order;
/// on a parameter it gives that input's kind. A kind is a family name
/// (see <c>PortFamily</c>, case-insensitive: "viewpoint", "item", "text", …)
/// optionally followed by <c>*</c> for a list or <c>**</c> for a list of lists.
/// Purely advisory.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class PortKindsAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="kinds">Kind strings, e.g. "viewpoint*", "text*", "integer".</param>
    public PortKindsAttribute(params string[] kinds)
    {
        Kinds = kinds ?? Array.Empty<string>();
    }

    /// <summary>The kind strings, in port order.</summary>
    public string[] Kinds { get; }
}

/// <summary>
/// Makes a list-typed parameter a multi-input socket: the editor lets any number of wires connect to it, drawn as
/// one pill-shaped socket, and the node receives their values combined into one list. Behaviour is exact for the
/// old single-wire case — with one wire the value arrives untouched (nesting and replication included), so adding
/// the attribute to an existing parameter never changes a saved graph; with two or more, list-valued wires
/// contribute their elements and other wires contribute themselves, in the order the wires were made. Ignored on
/// parameters that are not list-typed. Purely advisory: never changes the definition id.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class MultiInputAttribute : Attribute
{
}
