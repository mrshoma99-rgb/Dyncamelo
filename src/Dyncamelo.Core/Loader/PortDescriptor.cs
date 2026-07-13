using System;
using System.Collections.Generic;

namespace Dyncamelo.Core.Loader;

/// <summary>
/// Static description of one port of a zero-touch node definition.
/// </summary>
public class PortDescriptor
{
    /// <summary>Creates a descriptor.</summary>
    /// <param name="name">Port name (parameter name or output key).</param>
    /// <param name="type">Declared CLR type of the port.</param>
    public PortDescriptor(string name, Type type)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Type = type ?? throw new ArgumentNullException(nameof(type));
    }

    /// <summary>Port name.</summary>
    public string Name { get; }

    /// <summary>Declared CLR type.</summary>
    public Type Type { get; }

    /// <summary>Description/tooltip text.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>True when the underlying parameter is optional.</summary>
    public bool HasDefault { get; set; }

    /// <summary>Compile-time default value of an optional parameter.</summary>
    public object? DefaultValue { get; set; }

    /// <summary>
    /// Allowed values for a choice parameter (from <see cref="NodeChoicesAttribute"/>),
    /// or <c>null</c> for a free port. Drives the editor's dropdown.
    /// </summary>
    public IReadOnlyList<string>? Choices { get; set; }
}
