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
