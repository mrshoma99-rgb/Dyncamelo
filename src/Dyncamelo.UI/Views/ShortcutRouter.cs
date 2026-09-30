using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Dyncamelo.Core.Editing;
using Dyncamelo.UI.Mvvm;

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

    /// <summary>Builds the lookup from the default shortcuts; ids in <paramref name="skip"/> are left to special handling.</summary>
    public ShortcutRouter(IEnumerable<string>? skip = null)
        : this(new Keymap(), skip)
    {
    }

    /// <summary>Builds the lookup from the shortcuts in force (defaults plus the user's rebindings).</summary>
    /// <param name="keymap">The keymap to honour.</param>
    /// <param name="skip">Command ids left to special handling.</param>
    public ShortcutRouter(Keymap keymap, IEnumerable<string>? skip = null)
    {
        _skip = new HashSet<string>(skip ?? new string[0], StringComparer.Ordinal);
        foreach (var pair in keymap.Chords())
        {
            if (!_skip.Contains(pair.Value.Id))
            {
                _map[pair.Key] = pair.Value;
            }
        }
    }

    /// <summary>The chord a key press makes (the same form the catalogue and the keymap use).</summary>
    public static KeyChord ChordOf(Key key, ModifierKeys modifiers) => new KeyChord(
        NameOf(key),
        (modifiers & ModifierKeys.Control) != 0,
        (modifiers & ModifierKeys.Shift) != 0,
        (modifiers & ModifierKeys.Alt) != 0);

    /// <summary>True for keys that are only modifiers (Ctrl, Shift, Alt, Windows) and never a chord on their own.</summary>
    public static bool IsModifierKey(Key key) =>
        key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftShift || key == Key.RightShift ||
        key == Key.LeftAlt || key == Key.RightAlt || key == Key.LWin || key == Key.RWin || key == Key.System || key == Key.None;

    /// <summary>The command bound to the pressed chord, or null.</summary>
    public CommandInfo? Find(Key key, ModifierKeys modifiers)
    {
        return _map.TryGetValue(ChordOf(key, modifiers), out var info) ? info : null;
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
        return command != null && CommandInvoker.Run(command, routedTarget);
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
