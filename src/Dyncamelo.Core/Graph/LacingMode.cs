namespace Dyncamelo.Core.Graph;

/// <summary>
/// Replication ("lacing") strategy used when a node receives lists on inputs
/// whose declared type is scalar. Matches Dynamo's replication semantics.
/// </summary>
public enum LacingMode
{
    /// <summary>Engine default; currently an alias for <see cref="Shortest"/>.</summary>
    Auto = 0,

    /// <summary>Zip replicated arguments index-by-index; result length is the shortest list.</summary>
    Shortest = 1,

    /// <summary>Zip replicated arguments; shorter lists repeat their last element to reach the longest length.</summary>
    Longest = 2,

    /// <summary>Nested loops over all replicated arguments; the leftmost replicated input is the outermost loop.</summary>
    CrossProduct = 3,
}
