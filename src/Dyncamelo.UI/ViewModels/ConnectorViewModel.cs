using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Types;
using Dyncamelo.UI.Mvvm;
using Dyncamelo.UI.Services;

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
    private readonly PortKind _declaredKind;
    private PortKind _kind;
    private double _socketOpacity = 1d;

    /// <summary>Creates the wrapper.</summary>
    /// <param name="node">Owning node view model.</param>
    /// <param name="port">The wrapped port.</param>
    public ConnectorViewModel(NodeViewModel node, PortModel port)
    {
        Node = node;
        Port = port;
        _declaredKind = PortKinds.FromPort(port);
        _kind = _declaredKind;
        DisconnectCommand = new RelayCommand(
            () => Node.Owner.DisconnectConnectorCommand.Execute(this),
            () => IsConnected);
        SetLevelCommand = new RelayCommand<string>(SetLevel);
        Port.PropertyChanged += OnPortPropertyChanged;
    }

    /// <summary>Stops listening to the port (called when the owning node view model is discarded).</summary>
    public void Detach()
    {
        Port.PropertyChanged -= OnPortPropertyChanged;
    }

    // Undo/redo (and any other model-side edit) changes the port behind our back.
    private void OnPortPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PortModel.UserValue):
            case nameof(PortModel.HasUserValue):
                OnPropertyChanged(nameof(SelectedChoice));
                break;
            case nameof(PortModel.UseLevels):
                RaiseLevelsChanged();
                break;
        }
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
            var text = Port.Name + " : " + FriendlyTypeName(Port.DeclaredType) + (_kind.Family == PortFamily.Any ? string.Empty : "  (" + KindText + ")");
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

    // ----- type language: colour = family, shape = structure -------------------

    /// <summary>The port's current kind: declared, or refined from its last value / upstream wire when the declaration says "any".</summary>
    public PortKind Kind => _kind;

    /// <summary>Colour family of the socket and of wires leaving it.</summary>
    public PortFamily Family => _kind.Family;

    /// <summary>Structure (item, list, nested list, unknown) — the socket's shape.</summary>
    public PortDepth Depth => _kind.Depth;

    /// <summary>Shared frozen brush for the family.</summary>
    public Brush FamilyBrush => PortBrushes.For(_kind.Family);

    /// <summary>Shared frozen 10×10 glyph for the structure.</summary>
    public Geometry SocketGeometry => PortBrushes.Glyph(_kind.Depth);

    /// <summary>True for an optional, unwired input: drawn as a hollow ring.</summary>
    public bool IsHollow => IsOptional && !_isConnected;

    /// <summary>Socket opacity: dimmed while a wire drag makes this socket a poor target.</summary>
    public double SocketOpacity
    {
        get => _socketOpacity;
        set => SetProperty(ref _socketOpacity, value);
    }

    /// <summary>Family and structure as text, for tooltips ("Viewpoint list").</summary>
    public string KindText
    {
        get
        {
            var text = _kind.Family == PortFamily.Any ? "any" : _kind.Family.ToString().ToLowerInvariant();
            switch (_kind.Depth)
            {
                case PortDepth.List: return text + " list";
                case PortDepth.Nested: return text + " list of lists";
                default: return text;
            }
        }
    }

    /// <summary>Re-reads the kind from the output's last value when the declaration is untyped.</summary>
    public void RefreshObservedKind()
    {
        if (_declaredKind.Family != PortFamily.Any && _declaredKind.Depth != PortDepth.Unknown)
        {
            return;
        }

        SetKind(PortKinds.Observe(Port.Value, _declaredKind));
    }

    /// <summary>Adopts an upstream port's kind (an untyped input takes the colour of what feeds it).</summary>
    public void InheritKind(PortKind upstream)
    {
        if (_declaredKind.Family != PortFamily.Any && _declaredKind.Depth != PortDepth.Unknown)
        {
            return;
        }

        SetKind(upstream.Family == PortFamily.Any && upstream.Depth == PortDepth.Unknown ? _declaredKind : upstream);
    }

    private void SetKind(PortKind kind)
    {
        if (_kind.Equals(kind))
        {
            return;
        }

        _kind = kind;
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Family));
        OnPropertyChanged(nameof(Depth));
        OnPropertyChanged(nameof(FamilyBrush));
        OnPropertyChanged(nameof(SocketGeometry));
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(ToolTip));
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
                OnPropertyChanged(nameof(IsHollow));
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
