using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CamelGraph.Core.Graph;

namespace CamelGraph.Core.Editing;

/// <summary>What a name input offers to pick from.</summary>
public enum ModelDataKind
{
    /// <summary>The property tabs (categories) of the element: "Element", "Item", "TimeLiner"…</summary>
    Tab,

    /// <summary>The properties inside one tab of the element.</summary>
    Property,
}

/// <summary>
/// Says that a text input can be filled from the data of the element wired or picked into another input of the same node
/// (declared by <c>[NodeTabChoice]</c> and <c>[NodePropertyChoice]</c>). The list is only ever built from that element, and only
/// when the user asks for it with the search button: typing the name by hand always works.
/// </summary>
public sealed class ModelDataChoice
{
    /// <summary>Creates the description.</summary>
    /// <param name="kind">Tabs, or the properties of one tab.</param>
    /// <param name="from">The name of the input that carries the element (or elements) to read.</param>
    /// <param name="tab">For <see cref="ModelDataKind.Property"/>: the name of the input that holds the tab.</param>
    /// <param name="includeAncestors">True when the node also looks at the element's parents (the data may sit above the geometry).</param>
    public ModelDataChoice(ModelDataKind kind, string from, string? tab = null, bool includeAncestors = false)
    {
        Kind = kind;
        From = from ?? throw new ArgumentNullException(nameof(from));
        Tab = tab ?? string.Empty;
        IncludeAncestors = includeAncestors;
    }

    /// <summary>Tabs, or the properties of one tab.</summary>
    public ModelDataKind Kind { get; }

    /// <summary>The input that carries the element to read, or <see cref="CamelGraph.Core.Loader.NodeDataSource.Selection"/>.</summary>
    public string From { get; }

    /// <summary>True when the search reads the elements selected in the host right now instead of an input of the node.</summary>
    public bool FromSelection => string.Equals(From, CamelGraph.Core.Loader.NodeDataSource.Selection, StringComparison.Ordinal);

    /// <summary>For properties: the input that holds the tab.</summary>
    public string Tab { get; }

    /// <summary>True when the element's parents are read as well.</summary>
    public bool IncludeAncestors { get; }
}

/// <summary>Stands for "the elements selected in the host right now" as the source of a search; the host's catalog reads the selection.</summary>
public sealed class ModelSelectionSource
{
    private ModelSelectionSource()
    {
    }

    /// <summary>The one instance.</summary>
    public static ModelSelectionSource Current { get; } = new ModelSelectionSource();
}

/// <summary>What a search of one element's data found.</summary>
public sealed class ModelDataListing
{
    /// <summary>Creates a listing.</summary>
    /// <param name="names">The distinct names found, in the order to show them.</param>
    /// <param name="scanned">How many elements were read.</param>
    /// <param name="total">How many elements the input carries (more than <paramref name="scanned"/> when the read was capped).</param>
    /// <param name="problem">Why there is nothing to show, or empty.</param>
    public ModelDataListing(IReadOnlyList<string> names, int scanned, int total, string problem = "")
    {
        Names = names ?? throw new ArgumentNullException(nameof(names));
        Scanned = scanned;
        Total = total;
        Problem = problem ?? string.Empty;
    }

    /// <summary>The distinct names found.</summary>
    public IReadOnlyList<string> Names { get; }

    /// <summary>How many elements were read.</summary>
    public int Scanned { get; }

    /// <summary>How many elements the input carries.</summary>
    public int Total { get; }

    /// <summary>Why there is nothing to show, or empty.</summary>
    public string Problem { get; }

    /// <summary>A listing that only explains why there is nothing.</summary>
    /// <param name="problem">The explanation.</param>
    public static ModelDataListing None(string problem) => new ModelDataListing(Array.Empty<string>(), 0, 0, problem);

    /// <summary>Joins the names read from several elements into one sorted list without repeats.</summary>
    /// <param name="perElement">The names of each element read.</param>
    /// <param name="total">How many elements the input carries.</param>
    public static ModelDataListing Union(IEnumerable<IEnumerable<string>> perElement, int total)
    {
        if (perElement == null)
        {
            throw new ArgumentNullException(nameof(perElement));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        var scanned = 0;
        foreach (var element in perElement)
        {
            scanned++;
            foreach (var name in element)
            {
                if (!string.IsNullOrWhiteSpace(name) && seen.Add(name))
                {
                    names.Add(name);
                }
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return new ModelDataListing(names, scanned, Math.Max(total, scanned));
    }
}

/// <summary>
/// What the property drop-down needs from the host application: the tabs and properties of the elements it is handed. It reads
/// nothing but those elements — never the whole model.
/// </summary>
public interface IModelPropertyCatalog
{
    /// <summary>Lists the tabs of the elements, or the properties inside one tab of them.</summary>
    /// <param name="source">The value of the element input: a stored pick ("nw:…"), an element, or a list of them.</param>
    /// <param name="choice">What to list, and whether parents are read as well.</param>
    /// <param name="tab">For properties: the tab to look in.</param>
    ModelDataListing List(object? source, ModelDataChoice choice, string? tab);
}

/// <summary>Where the host publishes its <see cref="IModelPropertyCatalog"/>.</summary>
public static class ModelPropertyHost
{
    /// <summary>The active catalog, or null when the host offers none (the search button then explains that).</summary>
    public static IModelPropertyCatalog? Current { get; set; }

    /// <summary>The most elements one search reads; the rest are counted, not read.</summary>
    public const int MaxElementsRead = 100;
}

/// <summary>Finds what a name input's search button has to look at: the element input of the same node, nothing else.</summary>
public static class ModelDataScope
{
    /// <summary>The element input a name input reads from, or null.</summary>
    /// <param name="port">The name input.</param>
    public static PortModel? ScopePort(PortModel port)
    {
        if (port == null)
        {
            throw new ArgumentNullException(nameof(port));
        }

        var choice = port.DataChoice;
        return choice == null || choice.FromSelection
            ? null
            : port.Owner.InPorts.FirstOrDefault(p => string.Equals(p.Name, choice.From, StringComparison.Ordinal));
    }

    /// <summary>
    /// What the element input holds right now: the value of the node(s) wired to it as of the last run, or the elements picked on it.
    /// </summary>
    /// <param name="port">The name input whose search button was pressed.</param>
    /// <param name="problem">Why there is nothing to read (set when the result is null).</param>
    /// <returns>The element, a list of them, or a stored pick; null when there is nothing to read yet.</returns>
    public static object? Source(PortModel port, out string problem)
    {
        problem = string.Empty;
        if (port.DataChoice != null && port.DataChoice.FromSelection)
        {
            return ModelSelectionSource.Current;
        }

        var scope = ScopePort(port);
        if (scope == null)
        {
            problem = "This node has no '" + (port.DataChoice?.From ?? "element") + "' input to read from.";
            return null;
        }

        var graph = scope.Owner.Graph;
        var wires = graph?.FindConnectionsInto(scope) ?? Array.Empty<ConnectionModel>();
        if (wires.Count > 0)
        {
            var values = wires.Where(w => !w.IsMuted).Select(w => w.Source.Value).Where(v => v != null).ToList();
            if (values.Count == 0)
            {
                problem = "The element wired to '" + scope.Name + "' has not been computed yet. Run the graph (or pick the element on the node itself), then search.";
                return null;
            }

            return values.Count == 1 ? values[0] : values;
        }

        if (scope.HasUserValue && scope.UserValue is string stored && stored.Length > 0)
        {
            return stored;
        }

        problem = "Pick an element on '" + scope.Name + "' (or wire one in), then search its " +
                  (port.DataChoice!.Kind == ModelDataKind.Tab ? "tabs" : "properties") + ".";
        return null;
    }

    /// <summary>The text another input of the node holds right now (wired value, pinned value or default); empty when none.</summary>
    /// <param name="port">The input.</param>
    public static string TextOf(PortModel port)
    {
        if (port == null)
        {
            throw new ArgumentNullException(nameof(port));
        }

        var wire = port.Owner.Graph?.FindConnectionsInto(port).FirstOrDefault(w => !w.IsMuted);
        if (wire != null)
        {
            return wire.Source.Value as string ?? string.Empty;
        }

        if (port.HasUserValue)
        {
            return port.UserValue as string ?? string.Empty;
        }

        return port.HasDefault ? port.DefaultValue as string ?? string.Empty : string.Empty;
    }

    /// <summary>The tab a property search looks in (the value of the node's tab input).</summary>
    /// <param name="port">The property input whose search button was pressed.</param>
    public static string TabOf(PortModel port)
    {
        var choice = port.DataChoice;
        if (choice == null || choice.Kind != ModelDataKind.Property)
        {
            return string.Empty;
        }

        var tabPort = port.Owner.InPorts.FirstOrDefault(p => string.Equals(p.Name, choice.Tab, StringComparison.Ordinal));
        return tabPort == null ? string.Empty : TextOf(tabPort).Trim();
    }

    /// <summary>
    /// Runs the search of a name input: reads the element input of the node and lists its tabs or properties. Does nothing else — no
    /// other element of the model is looked at.
    /// </summary>
    /// <param name="port">The name input whose search button was pressed.</param>
    public static ModelDataListing Search(PortModel port)
    {
        if (port == null)
        {
            throw new ArgumentNullException(nameof(port));
        }

        var choice = port.DataChoice;
        if (choice == null)
        {
            return ModelDataListing.None("This input has nothing to search.");
        }

        var catalog = ModelPropertyHost.Current;
        if (catalog == null)
        {
            return ModelDataListing.None("Element data can be searched in Navisworks only.");
        }

        var tab = string.Empty;
        if (choice.Kind == ModelDataKind.Property)
        {
            tab = TabOf(port);
            if (tab.Length == 0)
            {
                return ModelDataListing.None("Choose the tab first (the '" + choice.Tab + "' input), then search its properties.");
            }
        }

        var source = Source(port, out var problem);
        if (source == null)
        {
            return ModelDataListing.None(problem);
        }

        try
        {
            return catalog.List(source, choice, tab);
        }
        catch (Exception ex)
        {
            return ModelDataListing.None("The element's data could not be read: " + ex.Message);
        }
    }

    /// <summary>Counts the elements in a value (an element, a list or a stored pick), for the host's catalog to cap its read.</summary>
    /// <param name="value">The value.</param>
    public static int CountOf(object? value)
    {
        switch (value)
        {
            case null:
                return 0;
            case string text:
                return ModelPickerHost.CountOf(text);
            case IEnumerable list:
                var count = 0;
                foreach (var entry in list)
                {
                    count += CountOf(entry);
                }

                return count;
            default:
                return 1;
        }
    }
}
