using System;

namespace CamelGraph.Core.Loader;

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
    public NodeFunctionAttribute(CamelGraph.Core.Graph.NodeFunction function)
    {
        Function = function;
    }

    /// <summary>The declared functional role.</summary>
    public CamelGraph.Core.Graph.NodeFunction Function { get; }
}

/// <summary>
/// Declares what a node can do outside the model and the graph (starts programs, uses the network, changes existing files), so the
/// editor can warn before a graph from a file runs such a node. Put it on every node that does any of these.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class NodeEffectsAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="effects">What the node can do.</param>
    public NodeEffectsAttribute(CamelGraph.Core.Graph.NodeEffects effects)
    {
        Effects = effects;
    }

    /// <summary>What the node can do.</summary>
    public CamelGraph.Core.Graph.NodeEffects Effects { get; }
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

/// <summary>Where the tab / property search of a name input reads from, besides the element input of the same node.</summary>
public static class NodeDataSource
{
    /// <summary>
    /// The elements selected in the host application right now. For nodes that search the whole model and so have no element input:
    /// the search button then lists the tabs or properties of what is selected (and of nothing else) to pick a name from.
    /// </summary>
    public const string Selection = "@selection";
}

/// <summary>
/// Marks a text parameter as the name of a property tab (category) of the model element carried by another input of the same node.
/// The editor then shows a small search button next to the text box; pressing it lists the tabs of THAT element (never of the whole
/// model) in a drop-down. Typing the name by hand always works and nothing is read until the button is pressed. Purely advisory — the
/// parameter stays a string, so saved graphs and definition ids are unchanged.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class NodeTabChoiceAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="from">The name of the input (parameter) that carries the element or elements, or <see cref="NodeDataSource.Selection"/>.</param>
    public NodeTabChoiceAttribute(string from)
    {
        From = from ?? throw new ArgumentNullException(nameof(from));
    }

    /// <summary>The input that carries the element or elements to read.</summary>
    public string From { get; }

    /// <summary>True when the node also looks at the element's parents, so their tabs are offered too.</summary>
    public bool IncludeAncestors { get; set; }
}

/// <summary>
/// Marks a text parameter as the name of a property inside the tab chosen on another input, of the model element carried by a third
/// input of the same node. The search button lists the properties of that tab on THAT element only (see <see cref="NodeTabChoiceAttribute"/>).
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class NodePropertyChoiceAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="from">The name of the input (parameter) that carries the element or elements, or <see cref="NodeDataSource.Selection"/>.</param>
    /// <param name="tab">The name of the input (parameter) that holds the tab.</param>
    public NodePropertyChoiceAttribute(string from, string tab)
    {
        From = from ?? throw new ArgumentNullException(nameof(from));
        Tab = tab ?? throw new ArgumentNullException(nameof(tab));
    }

    /// <summary>The input that carries the element or elements to read.</summary>
    public string From { get; }

    /// <summary>The input that holds the tab.</summary>
    public string Tab { get; }

    /// <summary>True when the node also looks at the element's parents, so their properties are offered too.</summary>
    public bool IncludeAncestors { get; set; }
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
/// The node runs even when a node feeding it has failed: instead of the usual "Upstream failure" stop, every failed input
/// arrives as an <see cref="CamelGraph.Core.Execution.UpstreamError"/> carrying the failing node's message, and the node
/// decides what to do (see <c>Flow.Try</c>). Without the attribute a failed input stops the node, as always.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class CatchesUpstreamErrorsAttribute : Attribute
{
}

/// <summary>
/// The node wants to see the empty elements of a list it is mapped over. Normally a null element of a laced list never reaches the
/// node: that position gets a null result and one warning ("1 of 3 laced calls received a null element"). With this attribute the
/// null is passed to the parameter and the node answers it itself, so <c>IsNull</c> can say <c>true</c> and <c>String.IsBlank</c>
/// can treat a missing cell as blank. The parameter must be able to hold null (a reference or nullable type; on a plain value type
/// such as <c>double</c> the engine still reports "Null value passed to input"). Calls with a single value are not affected,
/// and neither is any other parameter. Purely a run-time behaviour: it never changes the definition id.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class AcceptsNullAttribute : Attribute
{
}

/// <summary>
/// An <c>object</c>-typed parameter that takes exactly one thing per call (a name, a point, a vector, a viewpoint, a value to
/// compare). Without it an <c>object</c> port means "anything" and receives a list whole; with it the port counts as rank 0, like a
/// <c>double</c>, so a list wired to it maps the node over the list (lacing) and the node is called once per element. Ignored
/// on parameters that are not declared <c>object</c>. The node must therefore not expect a list on this parameter; a graph can
/// still hand the whole list to the node by choosing a list level on the port. Purely a run-time behaviour: it never
/// changes the definition id.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class ScalarInputAttribute : Attribute
{
}

/// <summary>
/// The node reads live host state (the current selection, the open document, the list of selection sets) instead of only its
/// inputs, so its output can differ between two runs although nothing in the graph changed. The engine therefore runs it on
/// every run instead of serving its cached output; the nodes after it run again only when what it produced is different
/// from the previous run. A node group with such a node inside behaves the same way. Purely a run-time behaviour: it never
/// changes the definition id.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class LiveStateAttribute : Attribute
{
}

/// <summary>
/// An input or output of this node used to be called something else. Saved graphs store wires and typed-in values by port name, so
/// renaming a port would silently drop them; with this attribute a graph that still says the old name finds the
/// port now called the current name. Repeat the attribute for several ports.
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
/// converters (via <c>CamelGraph.Core.Types.TypeCoercion.RegisterConverter</c>)
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

/// <summary>What the browse button of a path parameter opens.</summary>
public enum NodePathMode
{
    /// <summary>An open-file dialog: the file must exist (a file the node reads).</summary>
    Open = 0,

    /// <summary>A save-file dialog: the file may not exist yet (a file the node writes or creates).</summary>
    Save = 1,

    /// <summary>A folder chooser (a directory the node reads from, writes into or lists).</summary>
    Folder = 2,
}

/// <summary>
/// Tells the editor what a <c>string</c> parameter that holds a path is, so its browse button opens the right dialog: an open
/// dialog for a file the node reads, a SAVE dialog (a file that does not exist yet can be chosen) for a file the node writes, a
/// folder chooser for a directory. A parameter with this attribute always gets the browse button, whatever it is called. Without
/// it the editor guesses from the parameter's name and the node's name (see <c>PathPicker</c>), which is right for most nodes but
/// not for all. Purely advisory — the parameter stays a string, so saved graphs and definition ids are unchanged.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false)]
public sealed class NodePathAttribute : Attribute
{
    /// <summary>Creates the attribute.</summary>
    /// <param name="mode">Which dialog the browse button opens.</param>
    public NodePathAttribute(NodePathMode mode)
    {
        Mode = mode;
    }

    /// <summary>Which dialog the browse button opens.</summary>
    public NodePathMode Mode { get; }

    /// <summary>
    /// The file types offered, as a Windows file dialog filter: <c>"Excel workbooks (*.xlsx)|*.xlsx|All files (*.*)|*.*"</c>.
    /// Empty offers all files. Ignored for <see cref="NodePathMode.Folder"/>. A save dialog adds the first listed extension when
    /// the user types a name without one.
    /// </summary>
    public string Filter { get; set; } = string.Empty;
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
