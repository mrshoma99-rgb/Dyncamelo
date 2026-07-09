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
