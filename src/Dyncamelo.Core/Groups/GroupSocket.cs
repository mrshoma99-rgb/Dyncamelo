using System;

namespace Dyncamelo.Core.Groups;

/// <summary>Which side of a node group a socket is on: inputs enter the group, outputs leave it.</summary>
public enum SocketSide
{
    /// <summary>A value that comes in (left side of an instance; an output of the Group Input node inside).</summary>
    Input,

    /// <summary>A value that goes out (right side of an instance; an input of the Group Output node inside).</summary>
    Output,
}

/// <summary>
/// One socket of a node group's interface. Its <see cref="Id"/> never changes, so renaming or reordering a socket keeps the wires
/// of every instance attached to it.
/// </summary>
public sealed class GroupSocket
{
    internal GroupSocket(Guid id, string name, string kind)
    {
        Id = id;
        Name = name;
        Kind = kind;
    }

    /// <summary>Stable identity.</summary>
    public Guid Id { get; }

    /// <summary>Name shown on the socket; unique on its side of the group.</summary>
    public string Name { get; internal set; }

    /// <summary>
    /// Kind hint ("number", "text*", …) that colours the socket and gives it an inline editor; empty means any value.
    /// Sockets carry whole values, so a list travels through as one value.
    /// </summary>
    public string Kind { get; internal set; }
}
