namespace Dyncamelo.Core.Graph;

/// <summary>
/// How graph runs are triggered. The engine itself is always invoked explicitly;
/// this setting tells the hosting UI whether mutations should schedule runs automatically.
/// </summary>
public enum RunType
{
    /// <summary>The user triggers runs explicitly (Run button).</summary>
    Manual = 0,

    /// <summary>Every dirty-marking mutation schedules a (debounced) run. Dynamo-like default.</summary>
    Automatic = 1,
}
