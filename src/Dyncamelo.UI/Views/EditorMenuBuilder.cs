using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dyncamelo.Core.Editing;

namespace Dyncamelo.UI.Views;

/// <summary>
/// Builds the header menu bar from <see cref="CommandCatalog"/>. The catalogue
/// is the single source of truth for titles, categories and shortcuts, so a
/// command shows up here (with its shortcut) as soon as it is registered and
/// a resolver supplies its <see cref="ICommand"/>.
/// </summary>
internal static class EditorMenuBuilder
{
    /// <summary>Fills <paramref name="menu"/> with one top-level item per non-empty category.</summary>
    /// <param name="menu">The menu bar to (re)fill.</param>
    /// <param name="topStyle">Style for the top-level items (opens downwards).</param>
    /// <param name="resolve">Maps a command id to its command, or null when it is not available.</param>
    /// <param name="commandTarget">Target for routed commands (the canvas).</param>
    /// <param name="isChecked">Current state of a toggle command, refreshed whenever its menu opens.</param>
    /// <param name="extend">Adds extra entries (submenus) to a category after its catalogue entries.</param>
    /// <param name="keymap">The shortcuts in force, shown beside each item; the defaults when null.</param>
    public static void Build(
        Menu menu,
        Style? topStyle,
        Func<string, ICommand?> resolve,
        IInputElement? commandTarget,
        Func<string, bool> isChecked,
        Action<string, MenuItem>? extend,
        Keymap? keymap = null)
    {
        keymap ??= new Keymap();
        menu.Items.Clear();
        foreach (var category in CommandCatalog.Categories)
        {
            var top = new MenuItem { Header = category };
            if (topStyle != null)
            {
                top.Style = topStyle;
            }

            var toggles = new List<KeyValuePair<string, MenuItem>>();
            string? previousGroup = null;
            foreach (var info in CommandCatalog.All)
            {
                if (info.Category != category)
                {
                    continue;
                }

                var command = resolve(info.Id);
                if (command == null)
                {
                    continue;
                }

                var group = info.Id.Substring(0, info.Id.IndexOf('.'));
                if (previousGroup != null && previousGroup != group)
                {
                    top.Items.Add(new Separator());
                }

                previousGroup = group;

                var item = new MenuItem
                {
                    Header = new TextBlock { Text = info.Title },
                    Command = command,
                    InputGestureText = keymap.ShortcutOf(info.Id) ?? string.Empty,
                    IsCheckable = info.IsToggle,
                };
                if (command is RoutedCommand)
                {
                    item.CommandTarget = commandTarget;
                }

                if (info.IsToggle)
                {
                    toggles.Add(new KeyValuePair<string, MenuItem>(info.Id, item));
                }

                top.Items.Add(item);
            }

            extend?.Invoke(category, top);
            if (top.Items.Count == 0)
            {
                continue;
            }

            if (toggles.Count > 0)
            {
                top.SubmenuOpened += (sender, args) =>
                {
                    foreach (var toggle in toggles)
                    {
                        toggle.Value.IsChecked = isChecked(toggle.Key);
                    }
                };
            }

            menu.Items.Add(top);
        }
    }
}
