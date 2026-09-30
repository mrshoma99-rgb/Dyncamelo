using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using Dyncamelo.Core.Editing;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>One line of the command palette: a command to run, or a setting to jump to.</summary>
public sealed class PaletteEntry
{
    /// <summary>Creates an entry.</summary>
    public PaletteEntry(string id, string title, string category, string shortcut, bool isSetting)
    {
        Id = id;
        Title = title;
        Category = category;
        Shortcut = shortcut;
        IsSetting = isSetting;
    }

    /// <summary>Catalogue command id, or the settings id when <see cref="IsSetting"/>.</summary>
    public string Id { get; }

    /// <summary>Name shown in the list.</summary>
    public string Title { get; }

    /// <summary>Menu the command lives in ("Settings" for preferences).</summary>
    public string Category { get; }

    /// <summary>The shortcut in force, or empty.</summary>
    public string Shortcut { get; }

    /// <summary>True when the entry opens a preference instead of running a command.</summary>
    public bool IsSetting { get; }

    /// <summary>True when there is a shortcut to show.</summary>
    public bool HasShortcut => Shortcut.Length > 0;
}

/// <summary>A preference in the Settings page (toggle or choice), bound to the editor property that owns it.</summary>
public sealed class SettingItemViewModel : ObservableObject
{
    private readonly Func<object> _get;
    private readonly Action<object> _set;

    /// <summary>Creates the item.</summary>
    /// <param name="descriptor">What the setting is.</param>
    /// <param name="get">Reads the current value (bool for toggles, the option value for choices).</param>
    /// <param name="set">Writes a new value.</param>
    public SettingItemViewModel(SettingDescriptor descriptor, Func<object> get, Action<object> set)
    {
        Descriptor = descriptor;
        _get = get;
        _set = set;
        OptionLabels = descriptor.Options.Select(o => o.Label).ToList();
        ResetCommand = new RelayCommand(Reset, () => IsModified);
    }

    /// <summary>The catalogue entry.</summary>
    public SettingDescriptor Descriptor { get; }

    /// <summary>Stable id.</summary>
    public string Id => Descriptor.Id;

    /// <summary>Short name.</summary>
    public string Title => Descriptor.Title;

    /// <summary>What it changes.</summary>
    public string Description => Descriptor.Description;

    /// <summary>Section it belongs to.</summary>
    public string Section => Descriptor.Section;

    /// <summary>True for on/off settings.</summary>
    public bool IsToggle => Descriptor.Kind == SettingKind.Toggle;

    /// <summary>True for settings with a set of named values.</summary>
    public bool IsChoice => Descriptor.Kind == SettingKind.Choice;

    /// <summary>The labels of a choice.</summary>
    public IReadOnlyList<string> OptionLabels { get; }

    /// <summary>The value of a toggle.</summary>
    public bool BoolValue
    {
        get => IsToggle && _get() is bool on && on;
        set
        {
            if (IsToggle && BoolValue != value)
            {
                _set(value);
                Refresh();
            }
        }
    }

    /// <summary>The value of a choice, as its label.</summary>
    public string SelectedLabel
    {
        get => IsChoice ? Descriptor.OptionFor(CurrentChoice)?.Label ?? string.Empty : string.Empty;
        set
        {
            var option = Descriptor.Options.FirstOrDefault(o => o.Label == value);
            if (IsChoice && option != null && option.Value != CurrentChoice)
            {
                _set(option.Value);
                Refresh();
            }
        }
    }

    /// <summary>True when the value differs from the default.</summary>
    public bool IsModified => IsToggle ? BoolValue != Descriptor.DefaultToggle : CurrentChoice != Descriptor.DefaultChoice;

    /// <summary>Puts the default back.</summary>
    public ICommand ResetCommand { get; }

    private string CurrentChoice => _get() as string ?? string.Empty;

    /// <summary>Re-reads the value after the underlying property changed elsewhere.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(SelectedLabel));
        OnPropertyChanged(nameof(IsModified));
    }

    private void Reset()
    {
        if (IsToggle)
        {
            BoolValue = Descriptor.DefaultToggle;
        }
        else
        {
            _set(Descriptor.DefaultChoice);
            Refresh();
        }
    }
}

/// <summary>One command in the Shortcuts page: its current chord and the buttons that change it.</summary>
public sealed class ShortcutRowViewModel : ObservableObject
{
    private string _shortcut = string.Empty;
    private bool _isCustom;
    private bool _isCapturing;
    private string _message = string.Empty;

    /// <summary>Creates the row.</summary>
    public ShortcutRowViewModel(CommandInfo info, Action<ShortcutRowViewModel> change, Action<ShortcutRowViewModel> clear, Action<ShortcutRowViewModel> reset)
    {
        Info = info;
        ChangeCommand = new RelayCommand(() => change(this));
        ClearCommand = new RelayCommand(() => clear(this));
        ResetCommand = new RelayCommand(() => reset(this));
    }

    /// <summary>The catalogue entry.</summary>
    public CommandInfo Info { get; }

    /// <summary>Command id.</summary>
    public string CommandId => Info.Id;

    /// <summary>Command name.</summary>
    public string Title => Info.Title;

    /// <summary>Menu the command is in.</summary>
    public string Category => Info.Category;

    /// <summary>The default chord, or empty.</summary>
    public string DefaultShortcut => Info.Shortcut ?? string.Empty;

    /// <summary>The chord in force, or empty when unbound.</summary>
    public string Shortcut
    {
        get => _shortcut;
        private set
        {
            if (SetProperty(ref _shortcut, value))
            {
                OnPropertyChanged(nameof(Display));
            }
        }
    }

    /// <summary>What the row shows for the chord: the chord, "Press keys…" while listening, or "none".</summary>
    public string Display => IsCapturing ? "Press the new shortcut…" : Shortcut.Length == 0 ? "none" : Shortcut;

    /// <summary>True when the user changed it from the default.</summary>
    public bool IsCustom
    {
        get => _isCustom;
        private set => SetProperty(ref _isCustom, value);
    }

    /// <summary>True while the row is waiting for a key press.</summary>
    public bool IsCapturing
    {
        get => _isCapturing;
        set
        {
            if (SetProperty(ref _isCapturing, value))
            {
                OnPropertyChanged(nameof(Display));
            }
        }
    }

    /// <summary>Why the last attempt was refused (or empty).</summary>
    public string Message
    {
        get => _message;
        set
        {
            if (SetProperty(ref _message, value))
            {
                OnPropertyChanged(nameof(HasMessage));
            }
        }
    }

    /// <summary>True when there is a message to show.</summary>
    public bool HasMessage => _message.Length > 0;

    /// <summary>Starts listening for a new chord.</summary>
    public ICommand ChangeCommand { get; }

    /// <summary>Removes the shortcut.</summary>
    public ICommand ClearCommand { get; }

    /// <summary>Restores the default shortcut.</summary>
    public ICommand ResetCommand { get; }

    /// <summary>Reads the chord in force from the keymap.</summary>
    public void Refresh(Keymap keymap)
    {
        Shortcut = keymap.ShortcutOf(Info.Id) ?? string.Empty;
        IsCustom = keymap.IsCustom(Info.Id);
    }
}
