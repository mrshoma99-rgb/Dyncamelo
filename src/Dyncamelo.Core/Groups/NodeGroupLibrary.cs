using System;
using System.Collections.Generic;
using System.Linq;
using Dyncamelo.Core.Graph;

namespace Dyncamelo.Core.Groups;

/// <summary>Event payload carrying a node group.</summary>
public sealed class NodeGroupEventArgs : EventArgs
{
    /// <summary>Creates the payload.</summary>
    public NodeGroupEventArgs(NodeGroup group)
    {
        Group = group;
    }

    /// <summary>The group.</summary>
    public NodeGroup Group { get; }
}

/// <summary>
/// Every node group of one document. The document's own graph and the bodies of all groups share it, so a group can be used
/// (and another group made) anywhere, and a change to a group reaches all of its instances.
/// </summary>
public sealed class NodeGroupLibrary
{
    private readonly List<NodeGroup> _groups = new List<NodeGroup>();

    internal NodeGroupLibrary(GraphModel root)
    {
        Root = root;
    }

    /// <summary>The document's own graph.</summary>
    public GraphModel Root { get; }

    /// <summary>The groups, in the order they were made.</summary>
    public IReadOnlyList<NodeGroup> Groups => _groups;

    /// <summary>Raised after a group joins the library.</summary>
    public event EventHandler<NodeGroupEventArgs>? GroupAdded;

    /// <summary>Raised after a group leaves the library.</summary>
    public event EventHandler<NodeGroupEventArgs>? GroupRemoved;

    /// <summary>The group with the given id, or null.</summary>
    public NodeGroup? Find(Guid id) => _groups.FirstOrDefault(g => g.Id == id);

    /// <summary>The group with the given name (ignoring case), or null.</summary>
    public NodeGroup? Find(string name) =>
        _groups.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Makes a new, empty group named <paramref name="name"/> (made unique when taken) and adds it to the library.</summary>
    public NodeGroup Create(string name)
    {
        var group = new NodeGroup(this, Guid.NewGuid(), UniqueName(name));
        group.CreateInterfaceNodes();
        Add(group);
        return group;
    }

    /// <summary>Adds a group that was built elsewhere (loading a file, undoing a removal).</summary>
    public void Add(NodeGroup group)
    {
        if (group == null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        if (!ReferenceEquals(group.Library, this))
        {
            throw new ArgumentException("The group belongs to another library.", nameof(group));
        }

        if (_groups.Contains(group))
        {
            return;
        }

        _groups.Add(group);
        GroupAdded?.Invoke(this, new NodeGroupEventArgs(group));
    }

    /// <summary>
    /// Removes a group. Refused (false) while an instance of it exists anywhere, since those instances would lose their
    /// definition; delete or ungroup them first.
    /// </summary>
    public bool Remove(NodeGroup group)
    {
        if (group == null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        if (!_groups.Contains(group) || InstancesOf(group).Count > 0)
        {
            return false;
        }

        _groups.Remove(group);
        GroupRemoved?.Invoke(this, new NodeGroupEventArgs(group));
        return true;
    }

    /// <summary>A name not used by another group: <paramref name="wanted"/>, else "wanted 2", "wanted 3", …</summary>
    public string UniqueName(string wanted, NodeGroup? except = null)
    {
        var baseName = string.IsNullOrWhiteSpace(wanted) ? "Node Group" : wanted.Trim();
        var name = baseName;
        var n = 2;
        while (_groups.Any(g => !ReferenceEquals(g, except) && string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            name = baseName + " " + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            n++;
        }

        return name;
    }

    /// <summary>The document's graph followed by the body of every group.</summary>
    public IEnumerable<GraphModel> AllGraphs()
    {
        yield return Root;
        foreach (var group in _groups)
        {
            yield return group.Graph;
        }
    }

    /// <summary>Every instance of a group, wherever it is used.</summary>
    public IReadOnlyList<GroupInstanceNode> InstancesOf(NodeGroup group)
    {
        var found = new List<GroupInstanceNode>();
        foreach (var graph in AllGraphs())
        {
            foreach (var node in graph.Nodes)
            {
                if (node is GroupInstanceNode instance && ReferenceEquals(instance.Definition, group))
                {
                    found.Add(instance);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// True when putting an instance of <paramref name="instanceOf"/> into the body of <paramref name="container"/>
    /// (null = the document's own graph) would make a group contain itself, directly or through other groups.
    /// </summary>
    public bool WouldCreateCycle(NodeGroup? container, NodeGroup instanceOf)
    {
        return container != null && (ReferenceEquals(container, instanceOf) || instanceOf.Uses(container));
    }
}
