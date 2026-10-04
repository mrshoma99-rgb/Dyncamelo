namespace CamelGraph.Core.Editing;

/// <summary>
/// What the model-element input of a node needs from the host application (Navisworks): read the current
/// selection into a storable value, describe a stored value, and select it again. The value is an opaque
/// string kept in the port's pinned value, so it is saved with the graph and turned back into model items by
/// the host's type converters when the graph runs.
/// </summary>
public interface IModelPicker
{
    /// <summary>Encodes the host's current selection, or returns null when nothing is selected.</summary>
    /// <param name="single">True to keep only the first selected item (a port that takes one item).</param>
    /// <param name="count">How many items the selection holds (before <paramref name="single"/> trimmed it).</param>
    string? CaptureSelection(bool single, out int count);

    /// <summary>A short label for a stored value ("Pipe-101 (+2)"), or an empty string.</summary>
    /// <param name="value">A value produced by <see cref="CaptureSelection"/>.</param>
    string Describe(string? value);

    /// <summary>Selects the stored items in the host; false when they cannot be found any more.</summary>
    /// <param name="value">A value produced by <see cref="CaptureSelection"/>.</param>
    bool Reveal(string? value);
}

/// <summary>Where the host publishes its <see cref="IModelPicker"/>.</summary>
public static class ModelPickerHost
{
    /// <summary>The active picker, or null when the host offers none (the editor then shows no picker).</summary>
    public static IModelPicker? Current { get; set; }

    /// <summary>Prefix marking a stored model value, so unrelated strings are never mistaken for one.</summary>
    public const string Prefix = "nw:";

    /// <summary>Separator between the item paths of one value.</summary>
    public const char Separator = ';';

    /// <summary>How many items a stored value holds (0 for anything else).</summary>
    public static int CountOf(string? value)
    {
        if (value == null || !value.StartsWith(Prefix, System.StringComparison.Ordinal) || value.Length == Prefix.Length)
        {
            return 0;
        }

        return value.Substring(Prefix.Length).Split(Separator).Length;
    }
}
