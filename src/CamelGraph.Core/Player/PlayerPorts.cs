using System;
using System.Collections.Generic;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Loader;

namespace CamelGraph.Core.Player;

/// <summary>
/// A node whose value the Player offers as a field of the script's form (a number, a slider, a toggle, a text or path, a
/// colour). The Player edits it through the same inline editors the nodes use, so the node hands over a detached input port
/// that stands for its value: the editors work on the port, and changes to the port are copied into the node.
/// </summary>
public interface IPlayerInputNode
{
    /// <summary>
    /// Creates the port the Player's editors work on (see <see cref="PlayerPorts.Create"/>). Its default is the value saved in
    /// the script; clearing it (Reset) brings that value back.
    /// </summary>
    PortModel CreatePlayerPort();
}

/// <summary>A node whose result the Player shows by default (the Watch nodes).</summary>
public interface IPlayerOutputNode
{
    /// <summary>The text to show as the node's result after a run (may span several lines).</summary>
    string PlayerText { get; }
}

/// <summary>Builds the detached ports that stand for the values of input nodes.</summary>
public static class PlayerPorts
{
    /// <summary>
    /// Creates an input port that is not part of any node's port list. Editing its value (the editors pin a user value on it)
    /// calls <paramref name="apply"/> with the value now in force: the pinned one, else the default.
    /// </summary>
    /// <param name="owner">The input node the port stands for.</param>
    /// <param name="name">Port name; a name ending in "folder" makes a path editor pick a directory.</param>
    /// <param name="declaredType">double, long, bool or string: picks the editor and how numbers are rounded.</param>
    /// <param name="initial">The value saved in the script: the port's default.</param>
    /// <param name="apply">Copies a value into the node.</param>
    /// <param name="range">Limits and step of a number editor, or null.</param>
    /// <param name="kindHint">"file" for a path, "colour" for a colour, else empty.</param>
    public static PortModel Create(
        NodeModel owner,
        string name,
        Type declaredType,
        object? initial,
        Action<object?> apply,
        NodeRangeAttribute? range = null,
        string kindHint = "")
    {
        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        if (apply == null)
        {
            throw new ArgumentNullException(nameof(apply));
        }

        var port = new PortModel(owner, name, declaredType, PortDirection.Input)
        {
            HasDefault = true,
            DefaultValue = initial,
            Range = range,
            KindHint = kindHint,
        };
        port.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(PortModel.UserValue) || args.PropertyName == nameof(PortModel.HasUserValue))
            {
                apply(port.HasUserValue ? port.UserValue : port.DefaultValue);
            }
        };
        return port;
    }

    /// <summary>
    /// A name the path editor treats as a folder: the node's own name when it already ends in "folder", "directory" or "dir",
    /// else that name followed by " folder".
    /// </summary>
    /// <param name="name">The node's name.</param>
    public static string FolderName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        var lower = trimmed.ToLowerInvariant();
        if (lower.EndsWith("folder", StringComparison.Ordinal) || lower.EndsWith("directory", StringComparison.Ordinal) ||
            lower.EndsWith("dir", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return (trimmed.Length == 0 ? "Output" : trimmed) + " folder";
    }

    /// <summary>Converts an editor value to a double (null and non-numbers give 0).</summary>
    public static double ToDouble(object? value)
    {
        try
        {
            return value == null ? 0d : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return 0d;
        }
    }
}
