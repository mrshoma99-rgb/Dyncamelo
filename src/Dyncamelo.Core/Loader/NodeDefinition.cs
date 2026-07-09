using System;
using System.Collections.Generic;
using System.Reflection;

namespace Dyncamelo.Core.Loader;

/// <summary>
/// Immutable description of one zero-touch node: identity (the mangled function
/// signature persisted in .dyc files), library metadata, port descriptors and
/// the <see cref="MethodInfo"/> to invoke.
/// </summary>
public class NodeDefinition
{
    /// <summary>Creates a definition. Produced by <see cref="AssemblyNodeLoader"/>.</summary>
    /// <param name="id">Mangled function signature, e.g. "Dyncamelo.Nodes.MathNodes.Atan2@double,double".</param>
    /// <param name="assemblyName">Simple name of the defining assembly.</param>
    /// <param name="method">The public static method the node invokes.</param>
    public NodeDefinition(string id, string assemblyName, MethodInfo method)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        AssemblyName = assemblyName ?? throw new ArgumentNullException(nameof(assemblyName));
        Method = method ?? throw new ArgumentNullException(nameof(method));
    }

    /// <summary>
    /// Stable serialized identity: <c>Namespace.Class.Method@paramType1,paramType2</c>.
    /// Overloads are unambiguous because parameter types are part of the id.
    /// </summary>
    public string Id { get; }

    /// <summary>Simple name of the assembly that defines the method.</summary>
    public string AssemblyName { get; }

    /// <summary>The method invoked when the node executes.</summary>
    public MethodInfo Method { get; }

    /// <summary>Display name (attribute override or "Class.Method").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Dot-separated library category path.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Description for the library browser and tooltips.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Extra search keywords for the library browser.</summary>
    public IReadOnlyList<string> SearchTags { get; set; } = Array.Empty<string>();

    /// <summary>Input port descriptors, one per method parameter.</summary>
    public IReadOnlyList<PortDescriptor> Inputs { get; set; } = Array.Empty<PortDescriptor>();

    /// <summary>Output port descriptors (one, or one per MultiReturn key).</summary>
    public IReadOnlyList<PortDescriptor> Outputs { get; set; } = Array.Empty<PortDescriptor>();

    /// <summary>MultiReturn dictionary keys, or null for single-output nodes.</summary>
    public IReadOnlyList<string>? MultiReturnKeys { get; set; }

    /// <summary>True when the method returns void (the node passes its first input through).</summary>
    public bool IsVoid => Method.ReturnType == typeof(void);

    /// <inheritdoc />
    public override string ToString() => Id;
}
