namespace Dyncamelo.Core.Graph;

/// <summary>
/// The functional role of a node, used to group the library (and tint the node
/// on the canvas) so it is obvious at a glance what a node does — mirroring
/// Dynamo's Create / Action / Query split.
/// </summary>
public enum NodeFunction
{
    /// <summary>Produces something new: a model element, a saved view/set/folder, an exported file, or a new data value.</summary>
    Create,

    /// <summary>Acts on or changes existing things or the scene: overrides, transforms, isolate, move, rename, delete, export side-effects.</summary>
    Modify,

    /// <summary>Reads and returns data without changing anything: property reads, search/find, counts, names, measures.</summary>
    Info,
}
