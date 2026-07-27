using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Types;
using Dyncamelo.UI.Mvvm;

namespace Dyncamelo.UI.ViewModels;

/// <summary>
/// Wraps one <see cref="PortModel"/> for the Nodify connector controls.
/// <see cref="Anchor"/> is written by the view (OneWayToSource) and read by
/// connection shapes; <see cref="IsConnected"/> is maintained by the editor view model.
/// </summary>
public class ConnectorViewModel : ObservableObject
{
    private Point _anchor;
    private bool _isConnected;

    /// <summary>Creates the wrapper.</summary>
    /// <param name="node">Owning node view model.</param>
    /// <param name="port">The wrapped port.</param>
    public ConnectorViewModel(NodeViewModel node, PortModel port)
    {
        Node = node;
        Port = port;
        DisconnectCommand = new RelayCommand(
            () => Node.Owner.DisconnectConnectorCommand.Execute(this),
            () => IsConnected);
        SetLevelCommand = new RelayCommand<string>(SetLevel);
    }

    /// <summary>Owning node view model.</summary>
    public NodeViewModel Node { get; }

    /// <summary>The wrapped Core port.</summary>
    public PortModel Port { get; }

    /// <summary>Port name shown next to the connector dot.</summary>
    public string Title => Port.Name;

    /// <summary>True for input ports (rendered on the left side of the node).</summary>
    public bool IsInput => Port.Direction == PortDirection.Input;

    /// <summary>
    /// True for input ports that carry a default value (usable while unconnected).
    /// Rendered as a hollow/dimmed connector; required inputs are filled.
    /// </summary>
    public bool IsOptional => IsInput && Port.HasDefault;

    /// <summary>Tooltip: name, declared type, required/optional marker and description.</summary>
    public string ToolTip
    {
        get
        {
            var text = Port.Name + " : " + FriendlyTypeName(Port.DeclaredType);
            if (IsInput)
            {
                text += Port.HasDefault
                    ? "\noptional (default: " + TypeCoercion.FormatValue(Port.DefaultValue) + ")"
                    : "\nrequired";
            }

            if (Port.Description.Length > 0)
            {
                text += "\n" + Port.Description;
            }

            return text;
        }
    }

    /// <summary>Removes every wire touching this port (context menu "Disconnect").</summary>
    public ICommand DisconnectCommand { get; }

    // ----- List@Level (Dynamo's @L) --------------------------------------------

    /// <summary>
    /// Whether this input consumes the incoming list at a chosen level
    /// (counted from the innermost) instead of its declared rank. Enabling
    /// defaults to @L2 — "feed me the lists of items" — the most common use.
    /// </summary>
    public bool UseLevels
    {
        get => Port.UseLevels;
        set
        {
            Port.SetLevels(value, value && Port.Level < 1 ? 2 : Port.Level, Port.KeepListStructure);
            RaiseLevelsChanged();
        }
    }

    /// <summary>The active level (1 = items, 2 = lists of items, …; -1 = off).</summary>
    public int Level => Port.Level;

    /// <summary>
    /// With levels on: true preserves the incoming nesting in the output,
    /// false (the Dynamo default) flattens the replicated levels into one list.
    /// </summary>
    public bool KeepListStructure
    {
        get => Port.KeepListStructure;
        set
        {
            Port.SetLevels(Port.UseLevels, Port.Level, value);
            RaiseLevelsChanged();
        }
    }

    /// <summary>Selects the level from a menu parameter ("1".."4"); turns levels on.</summary>
    public ICommand SetLevelCommand { get; }

    /// <summary>Badge text next to the port name ("@L2"), empty while levels are off.</summary>
    public string LevelLabel => Port.UseLevels && Port.Level >= 1 ? "@L" + Port.Level : string.Empty;

    /// <summary>True when the level badge renders.</summary>
    public bool HasLevels => LevelLabel.Length > 0;

    /// <summary>True when the active level is 1 (menu check mark).</summary>
    public bool IsLevel1 => Port.UseLevels && Port.Level == 1;

    /// <summary>True when the active level is 2 (menu check mark).</summary>
    public bool IsLevel2 => Port.UseLevels && Port.Level == 2;

    /// <summary>True when the active level is 3 (menu check mark).</summary>
    public bool IsLevel3 => Port.UseLevels && Port.Level == 3;

    /// <summary>True when the active level is 4 (menu check mark).</summary>
    public bool IsLevel4 => Port.UseLevels && Port.Level == 4;

    private void SetLevel(string? parameter)
    {
        if (parameter != null && int.TryParse(parameter, out var level) && level >= 1)
        {
            Port.SetLevels(useLevels: true, level, Port.KeepListStructure);
            RaiseLevelsChanged();
        }
    }

    private void RaiseLevelsChanged()
    {
        OnPropertyChanged(nameof(UseLevels));
        OnPropertyChanged(nameof(Level));
        OnPropertyChanged(nameof(KeepListStructure));
        OnPropertyChanged(nameof(LevelLabel));
        OnPropertyChanged(nameof(HasLevels));
        OnPropertyChanged(nameof(IsLevel1));
        OnPropertyChanged(nameof(IsLevel2));
        OnPropertyChanged(nameof(IsLevel3));
        OnPropertyChanged(nameof(IsLevel4));
    }

    /// <summary>Graph-space position of the connector dot; written by the view.</summary>
    public Point Anchor
    {
        get => _anchor;
        set => SetProperty(ref _anchor, value);
    }

    /// <summary>True when at least one wire touches this port.</summary>
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (SetProperty(ref _isConnected, value))
            {
                // A wire hides the inline choice editor and vice-versa.
                OnPropertyChanged(nameof(ShowChoiceEditor));
                OnPropertyChanged(nameof(SelectedChoice));
            }
        }
    }

    /// <summary>True for an input port that declares a fixed set of choices (renders a dropdown).</summary>
    public bool IsChoice => IsInput && Port.Choices != null && Port.Choices.Count > 0;

    /// <summary>The allowed values for the dropdown (empty when this is not a choice port).</summary>
    public IReadOnlyList<string> Choices => Port.Choices ?? Array.Empty<string>();

    /// <summary>Show the inline dropdown only for an unconnected choice port.</summary>
    public bool ShowChoiceEditor => IsChoice && !IsConnected;

    /// <summary>
    /// The value shown in the choice dropdown: the pinned user value if set,
    /// otherwise the port's default. Setting it pins the choice (or clears it
    /// when set back to the default), marking the node dirty for re-evaluation.
    /// </summary>
    public string? SelectedChoice
    {
        get
        {
            var current = Port.HasUserValue ? Port.UserValue : Port.DefaultValue;
            return current?.ToString();
        }

        set
        {
            if (value == null)
            {
                Port.ClearUserValue();
            }
            else
            {
                Port.SetUserValue(value);
            }

            OnPropertyChanged();
        }
    }

    private static string FriendlyTypeName(System.Type type)
    {
        if (type == typeof(double))
        {
            return "number";
        }

        if (type == typeof(long) || type == typeof(int))
        {
            return "integer";
        }

        if (type == typeof(bool))
        {
            return "boolean";
        }

        if (type == typeof(string))
        {
            return "string";
        }

        if (type == typeof(object))
        {
            return "var";
        }

        return type.Name;
    }
}
