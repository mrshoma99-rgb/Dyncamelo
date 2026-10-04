using System;
using System.Collections.Generic;
using System.Linq;

namespace CamelGraph.Core.Editing;

/// <summary>
/// The shortcuts in force: the defaults of <see cref="CommandCatalog"/> with the user's overrides applied.
/// Menus, key handling, the help overlay and the palette all read from one keymap, so a rebinding shows up
/// everywhere at once. An override with an empty chord unbinds the command.
/// </summary>
public sealed class Keymap
{
    private static readonly HashSet<string> ReservedTextChords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+Z", "Ctrl+Y", "Ctrl+A", "Delete", "Space", "Backspace", "Enter", "Escape", "Tab",
    };

    private readonly Dictionary<string, string> _overrides;
    private readonly Dictionary<KeyChord, CommandInfo> _byChord = new Dictionary<KeyChord, CommandInfo>();
    private readonly Dictionary<string, string> _effective = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Builds the keymap from the catalogue defaults and the overrides (command id → chord, "" = unbound).</summary>
    public Keymap(IReadOnlyDictionary<string, string>? overrides = null)
    {
        _overrides = new Dictionary<string, string>(StringComparer.Ordinal);
        if (overrides != null)
        {
            foreach (var pair in overrides)
            {
                if (CommandCatalog.Find(pair.Key) != null)
                {
                    _overrides[pair.Key] = pair.Value ?? string.Empty;
                }
            }
        }

        foreach (var info in CommandCatalog.All)
        {
            var overridden = _overrides.TryGetValue(info.Id, out var chordText);
            var main = overridden ? chordText : info.Shortcut;
            if (Shortcuts.TryParse(main, out var chord))
            {
                _effective[info.Id] = chord.ToString();
                _byChord[chord] = info;
            }

            // The built-in alternate (Ctrl+Shift+Z for redo) goes away when the user picks their own chord.
            if (!overridden && Shortcuts.TryParse(info.Alternate, out var alternate))
            {
                _byChord[alternate] = info;
            }
        }
    }

    /// <summary>Command ids the user has rebound (or unbound).</summary>
    public IReadOnlyCollection<string> OverriddenIds => _overrides.Keys;

    /// <summary>The chord currently bound to a command ("Ctrl+Z"), or null when it has none.</summary>
    public string? ShortcutOf(string commandId) => _effective.TryGetValue(commandId, out var chord) ? chord : null;

    /// <summary>The second chord that also runs a command (only the built-in alternate while not rebound), or null.</summary>
    public string? AlternateOf(string commandId)
    {
        var info = CommandCatalog.Find(commandId);
        return info != null && info.Alternate != null && !_overrides.ContainsKey(commandId) ? info.Alternate : null;
    }

    /// <summary>The command a key press runs, or null.</summary>
    public CommandInfo? Find(KeyChord chord) => _byChord.TryGetValue(chord, out var info) ? info : null;

    /// <summary>Every (chord, command) pair in force.</summary>
    public IEnumerable<KeyValuePair<KeyChord, CommandInfo>> Chords() => _byChord;

    /// <summary>True when the command has left its default shortcut.</summary>
    public bool IsCustom(string commandId) => _overrides.ContainsKey(commandId);

    /// <summary>
    /// Whether <paramref name="chord"/> may be given to <paramref name="commandId"/>: null when it may, otherwise the reason.
    /// An empty chord (unbinding) is always allowed.
    /// </summary>
    public string? Validate(string commandId, string chord)
    {
        var info = CommandCatalog.Find(commandId);
        if (info == null)
        {
            return "Unknown command.";
        }

        if (string.IsNullOrWhiteSpace(chord))
        {
            return null;
        }

        if (!Shortcuts.TryParse(chord, out var parsed))
        {
            return "'" + chord + "' is not a valid shortcut.";
        }

        if (info.Scope == CommandScope.Global)
        {
            if (ReservedTextChords.Contains(parsed.ToString()))
            {
                return parsed + " edits text inside boxes, so a command that also works there cannot use it.";
            }

            if (!parsed.Ctrl && !parsed.Alt && !IsFunctionKey(parsed.Key))
            {
                return parsed + " is typed into text boxes, so a command that also works there needs Ctrl or Alt (or a function key).";
            }
        }

        var owner = Find(parsed);
        if (owner != null && !string.Equals(owner.Id, commandId, StringComparison.Ordinal))
        {
            return parsed + " is already used by \"" + owner.Title + "\".";
        }

        return null;
    }

    private static bool IsFunctionKey(string key)
    {
        return key.Length >= 2 && (key[0] == 'F' || key[0] == 'f') && int.TryParse(key.Substring(1), out var n) && n >= 1 && n <= 24;
    }

    /// <summary>Returns the overrides that result from binding one command (the keymap itself is immutable).</summary>
    public Dictionary<string, string> With(string commandId, string chord)
    {
        var next = new Dictionary<string, string>(_overrides, StringComparer.Ordinal);
        var info = CommandCatalog.Find(commandId);
        if (info == null)
        {
            return next;
        }

        chord = chord ?? string.Empty;
        if (Shortcuts.TryParse(chord, out var parsed))
        {
            chord = parsed.ToString();
        }

        // Binding back to the default is the same as having no override.
        if (Shortcuts.TryParse(info.Shortcut, out var original) && string.Equals(original.ToString(), chord, StringComparison.OrdinalIgnoreCase))
        {
            next.Remove(commandId);
        }
        else
        {
            next[commandId] = chord;
        }

        return next;
    }
}
