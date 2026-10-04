using System;
using System.Windows.Input;
using CamelGraph.UI.Mvvm;

namespace CamelGraph.UI.ViewModels;

/// <summary>What a row of the side list stands for; the view picks its marker from it.</summary>
public enum PanelItemKind
{
    /// <summary>A node that failed.</summary>
    Error,

    /// <summary>A node with a warning.</summary>
    Warning,

    /// <summary>An edit that is applied.</summary>
    Step,

    /// <summary>An undone edit that Redo would bring back.</summary>
    Future,

    /// <summary>The state before the first edit.</summary>
    Start,

    /// <summary>A saved view of the canvas.</summary>
    Bookmark,
}

/// <summary>One row of the list panel that shows problems, the undo history or bookmarks.</summary>
public sealed class PanelItemViewModel : ObservableObject
{
    private bool _isCurrent;

    /// <summary>Creates a row.</summary>
    /// <param name="kind">What the row is.</param>
    /// <param name="title">First line.</param>
    /// <param name="detail">Second line, or empty.</param>
    /// <param name="activate">Runs when the row is clicked.</param>
    /// <param name="remove">Runs when the row's remove button is clicked; null for rows that cannot be removed.</param>
    /// <param name="isCurrent">True for the row that marks where things stand now.</param>
    public PanelItemViewModel(PanelItemKind kind, string title, string detail, Action activate, Action? remove = null, bool isCurrent = false)
    {
        Kind = kind;
        Title = title;
        Detail = detail ?? string.Empty;
        _isCurrent = isCurrent;
        ActivateCommand = new RelayCommand(activate);
        RemoveCommand = remove == null ? null : new RelayCommand(remove);
    }

    /// <summary>What the row is.</summary>
    public PanelItemKind Kind { get; }

    /// <summary>First line.</summary>
    public string Title { get; }

    /// <summary>Second line.</summary>
    public string Detail { get; }

    /// <summary>True when there is a second line.</summary>
    public bool HasDetail => Detail.Length > 0;

    /// <summary>True for the row that marks the present state (the undo history's "you are here").</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    /// <summary>Clicking the row.</summary>
    public ICommand ActivateCommand { get; }

    /// <summary>The remove button, or null.</summary>
    public ICommand? RemoveCommand { get; }

    /// <summary>True when the row has a remove button.</summary>
    public bool CanRemove => RemoveCommand != null;
}
