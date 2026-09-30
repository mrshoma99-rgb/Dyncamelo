using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Dyncamelo.Core.Editing;

namespace Dyncamelo.UI.Views;

/// <summary>
/// Turns a key press into the catalogue command that owns it. Every shortcut of
/// <see cref="CommandCatalog"/> is honoured here, so a key can never work without
/// showing in the menus and help, nor be listed there and not work.
/// </summary>
public sealed class ShortcutRouter
{
    private readonly Dictionary<KeyChord, CommandInfo> _map = new Dictionary<KeyChord, CommandInfo>();
    private readonly HashSet<string> _skip;

    /// <summary>Builds the lookup from the catalogue; ids in <paramref name="skip"/> are left to special handling.</summary>
    public ShortcutRouter(IEnumerable<string>? skip = null)
    {
        _skip = new HashSet<string>(skip ?? new string[0], StringComparer.Ordinal);
        foreach (var pair in Shortcuts.All())
        {
            if (!_skip.Contains(pair.Value.Id))
            {
                _map[pair.Key] = pair.Value;
            }
        }
    }

    /// <summary>The command bound to the pressed chord, or null.</summary>
    public CommandInfo? Find(Key key, ModifierKeys modifiers)
    {
        var chord = new KeyChord(
            NameOf(key),
            (modifiers & ModifierKeys.Control) != 0,
            (modifiers & ModifierKeys.Shift) != 0,
            (modifiers & ModifierKeys.Alt) != 0);
        return _map.TryGetValue(chord, out var info) ? info : null;
    }

    /// <summary>
    /// Runs the command bound to the chord when the current focus allows it: global commands always,
    /// canvas commands never while a text box has focus, and unmodified single keys only on the canvas.
    /// </summary>
    /// <returns>True when a command ran (the key press is consumed).</returns>
    public bool TryDispatch(Key key, ModifierKeys modifiers, bool typing, bool canvasFocused, Func<string, ICommand?> resolve, IInputElement? routedTarget)
    {
        var info = Find(key, modifiers);
        if (info == null)
        {
            return false;
        }

        if (info.Scope == CommandScope.Canvas)
        {
            if (typing)
            {
                return false;
            }

            var chorded = (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0;
            if (!chorded && !canvasFocused)
            {
                return false;
            }
        }

        var command = resolve(info.Id);
        if (command == null)
        {
            return false;
        }

        if (command is RoutedCommand routed)
        {
            if (!routed.CanExecute(null, routedTarget))
            {
                return false;
            }

            routed.Execute(null, routedTarget);
            return true;
        }

        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    private static string NameOf(Key key)
    {
        // Digits are "D1".."D9" in WPF but "1".."9" in the catalogue.
        if (key >= Key.D0 && key <= Key.D9)
        {
            return ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return key.ToString();
    }
}
