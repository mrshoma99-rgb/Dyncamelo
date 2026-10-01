using System;
using System.Collections.Generic;
using System.Globalization;
using Dyncamelo.Core.Loader;
using Dyncamelo.Core.Types;

namespace Dyncamelo.Nodes.Portable;

/// <summary>What a <see cref="PropertySpec"/> reads from a model item.</summary>
public enum PropertySpecKind
{
    /// <summary>A property of the item, found by category and property name.</summary>
    Property,

    /// <summary>The virtual column "@Name": the item's display name.</summary>
    Name,

    /// <summary>The virtual column "@Path": the item's selection-tree path.</summary>
    Path,

    /// <summary>The virtual column "@Guid": the item's instance GUID.</summary>
    Guid,
}

/// <summary>
/// One requested column of Properties.ToTable / Model.Snapshot, written by the user as "Category.Property",
/// "Category|Property", a bare property name, or one of the virtual columns "@Name", "@Path" and "@Guid".
/// Pure parsing (no Navisworks types), so the rules are unit-tested.
/// </summary>
[IsVisibleInLibrary(false)]
public sealed class PropertySpec
{
    private PropertySpec(string header, PropertySpecKind kind, string? category, string property)
    {
        Header = header;
        Kind = kind;
        Category = category;
        Property = property;
    }

    /// <summary>The column header: exactly the text the user wrote (trimmed).</summary>
    public string Header { get; }

    /// <summary>Whether this is a property or one of the virtual columns.</summary>
    public PropertySpecKind Kind { get; }

    /// <summary>The category name, or null to search every category (first match wins). Always null for virtual columns.</summary>
    public string? Category { get; }

    /// <summary>The property name. Empty for virtual columns.</summary>
    public string Property { get; }

    /// <summary>
    /// Reads a spec. A "|" splits category and property at the first "|"; otherwise the first "." does; a name with
    /// neither is searched in every category. "@Name", "@Path" and "@Guid" (any case) are the virtual columns.
    /// </summary>
    /// <param name="text">The text the user wrote.</param>
    /// <returns>The parsed spec.</returns>
    /// <exception cref="ArgumentException">The text is empty, an unknown "@" column, or has an empty category or property.</exception>
    public static PropertySpec Parse(string? text)
    {
        if (!TryParse(text, out var spec, out var problem))
        {
            throw new ArgumentException(problem, nameof(text));
        }

        return spec!;
    }

    /// <summary>Like <see cref="Parse"/> but reports what is wrong instead of throwing.</summary>
    /// <param name="text">The text the user wrote.</param>
    /// <param name="spec">The parsed spec, or null.</param>
    /// <param name="problem">What is wrong, or null when the text was read.</param>
    /// <returns>True when the text was read.</returns>
    public static bool TryParse(string? text, out PropertySpec? spec, out string? problem)
    {
        spec = null;
        var trimmed = text == null ? string.Empty : text.Trim();
        if (trimmed.Length == 0)
        {
            problem = "the property name is empty. Write \"Category.Property\" (for example \"Element.Category\"), a property name, or @Name, @Path or @Guid";
            return false;
        }

        if (trimmed[0] == '@')
        {
            if (string.Equals(trimmed, "@Name", StringComparison.OrdinalIgnoreCase))
            {
                spec = new PropertySpec(trimmed, PropertySpecKind.Name, null, string.Empty);
            }
            else if (string.Equals(trimmed, "@Path", StringComparison.OrdinalIgnoreCase))
            {
                spec = new PropertySpec(trimmed, PropertySpecKind.Path, null, string.Empty);
            }
            else if (string.Equals(trimmed, "@Guid", StringComparison.OrdinalIgnoreCase))
            {
                spec = new PropertySpec(trimmed, PropertySpecKind.Guid, null, string.Empty);
            }
            else
            {
                problem = "'" + trimmed + "' is not a known virtual column. Use @Name, @Path or @Guid";
                return false;
            }

            problem = null;
            return true;
        }

        var bar = trimmed.IndexOf('|');
        var separator = bar >= 0 ? bar : trimmed.IndexOf('.');
        if (separator < 0)
        {
            spec = new PropertySpec(trimmed, PropertySpecKind.Property, null, trimmed);
            problem = null;
            return true;
        }

        var category = trimmed.Substring(0, separator).Trim();
        var property = trimmed.Substring(separator + 1).Trim();
        if (category.Length == 0 || property.Length == 0)
        {
            problem = "'" + trimmed + "' needs both a category and a property around the '" + trimmed[separator] +
                      "' (for example \"Element.Category\")";
            return false;
        }

        spec = new PropertySpec(trimmed, PropertySpecKind.Property, category, property);
        problem = null;
        return true;
    }

    /// <summary>Reads a whole list of specs, naming the node and the position of the first one that cannot be read.</summary>
    /// <param name="specs">The wired values; each must be text.</param>
    /// <param name="nodeName">The node asking, for the error messages.</param>
    /// <returns>The parsed specs in order (at least one).</returns>
    /// <exception cref="ArgumentNullException">The list is null.</exception>
    /// <exception cref="ArgumentException">The list is empty, or one entry is not readable text.</exception>
    public static List<PropertySpec> ParseList(IList<object?> specs, string nodeName)
    {
        if (specs == null)
        {
            throw new ArgumentNullException(
                nameof(specs),
                nodeName + " requires the properties to read. Wire a list of names such as \"Element.Category\" or \"@Name\" into the 'properties' input.");
        }

        if (specs.Count == 0)
        {
            throw new ArgumentException(
                nodeName + " requires at least one property name. Wire a list such as \"Element.Category\", \"@Name\" into the 'properties' input.",
                nameof(specs));
        }

        var result = new List<PropertySpec>(specs.Count);
        for (int i = 0; i < specs.Count; i++)
        {
            var entry = specs[i];
            var index = i.ToString(CultureInfo.InvariantCulture);
            if (entry == null)
            {
                throw new ArgumentException(nodeName + ": the property at index " + index + " (counting from 0) is empty.", nameof(specs));
            }

            var text = entry as string ?? TypeCoercion.FormatValue(entry);
            if (!TryParse(text, out var spec, out var problem))
            {
                throw new ArgumentException(nodeName + ": the property at index " + index + " (counting from 0): " + problem + ".", nameof(specs));
            }

            result.Add(spec!);
        }

        return result;
    }
}
