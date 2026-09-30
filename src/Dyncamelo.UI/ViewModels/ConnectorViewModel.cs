using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Dyncamelo.Core.Editing;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Player;
using Dyncamelo.Core.Types;
using Dyncamelo.UI.Mvvm;
using Dyncamelo.UI.Services;

namespace Dyncamelo.UI.ViewModels;

/// <summary>What a connector needs from whoever shows it: the node editor, or the Script Player's form.</summary>
public interface IConnectorHost
{
    /// <summary>True when sockets draw a type letter (the colour-blind aid).</summary>
    bool ColourBlindGlyphs { get; }

    /// <summary>Dialogs for browsing and confirming.</summary>
    Dyncamelo.UI.Services.IDialogService Dialogs { get; }

    /// <summary>The undo history that edits made through the connector are recorded in.</summary>
    Dyncamelo.Core.Editing.UndoManager History { get; }

    /// <summary>Shows a message to the user.</summary>
    void ReportStatus(string message);

    /// <summary>Shows a problem to the user.</summary>
    void ReportProblem(string message);
}

/// <summary>
/// Wraps one <see cref="PortModel"/> for the Nodify connector controls.
/// <see cref="Anchor"/> is written by the view (OneWayToSource) and read by
/// connection shapes; <see cref="IsConnected"/> is maintained by the editor view model.
/// </summary>
public class ConnectorViewModel : ObservableObject
{
    private readonly IConnectorHost? _host;
    private Point _anchor;
    private bool _isConnected;
    private PortKind _declaredKind;
    private PortKind _kind;
    private double _socketOpacity = 1d;

    /// <summary>Creates the wrapper.</summary>
    /// <param name="node">Owning node view model.</param>
    /// <param name="port">The wrapped port.</param>
    public ConnectorViewModel(NodeViewModel node, PortModel port)
        : this(node, port, null)
    {
    }

    /// <summary>
    /// Creates a connector that belongs to no node on a canvas — the field of the Script Player's form. It has the same inline
    /// editors, with the host taking the place of the editor for dialogs, messages and history.
    /// </summary>
    /// <param name="host">What the editors talk to.</param>
    /// <param name="port">The port the editors work on.</param>
    public ConnectorViewModel(IConnectorHost host, PortModel port)
        : this(null, port, host)
    {
    }

    private ConnectorViewModel(NodeViewModel? node, PortModel port, IConnectorHost? host)
    {
        Node = node!;
        _host = host;
        Port = port;
        _declaredKind = PortKinds.FromPort(port);
        _kind = _declaredKind;
        EditorKind = PortEditors.Resolve(port);
        NumberSpec = EditorKind == PortEditorKind.Number ? NumberEditSpec.FromPort(port) : null;
        ResetCommand = new RelayCommand(() => Port.ClearUserValue(), () => Port.HasUserValue);
        BrowseCommand = new RelayCommand(BrowsePath);
        CaptureModelCommand = new RelayCommand(CaptureModel, () => ModelPickerHost.Current != null);
        RevealModelCommand = new RelayCommand(RevealModel, () => HasModelValue && ModelPickerHost.Current != null);
        DisconnectCommand = new RelayCommand(
            () => Node?.Owner.DisconnectConnectorCommand.Execute(this),
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
                RaiseValueChanged();
                break;
            case nameof(PortModel.UseLevels):
                RaiseLevelsChanged();
                break;
            case nameof(PortModel.PlayerExposed):
                OnPropertyChanged(nameof(IsPlayerExposed));
                Node?.RaisePlayerBadge();
                break;
            case nameof(PortModel.Name):
            case nameof(PortModel.KindHint):
                RefreshFromPort();
                break;
        }
    }

    /// <summary>
    /// Re-reads everything derived from the port's name and kind hint — the label, colour, shape and inline editor — after a
    /// node group's socket was renamed or retyped.
    /// </summary>
    public void RefreshFromPort()
    {
        _declaredKind = PortKinds.FromPort(Port);
        _kind = _declaredKind;
        EditorKind = PortEditors.Resolve(Port);
        NumberSpec = EditorKind == PortEditorKind.Number ? NumberEditSpec.FromPort(Port) : null;
        foreach (var name in new[]
        {
            nameof(Title), nameof(ToolTip), nameof(Kind), nameof(Family), nameof(Depth), nameof(FamilyBrush), nameof(SocketGlyph),
            nameof(SocketGeometry), nameof(KindText), nameof(EditorKind), nameof(NumberSpec), nameof(ShowEditor), nameof(ShowPlainLabel),
            nameof(NumberMin), nameof(NumberMax), nameof(NumberSoftMin), nameof(NumberSoftMax), nameof(NumberStep), nameof(NumberIsInteger),
            nameof(NumberUnit), nameof(NumberValue),
        })
        {
            OnPropertyChanged(name);
        }

        RaiseValueChanged();
    }

    // ----- sockets of a node group's interface nodes ------------------------------------------

    private ICommand? _renameSocketCommand;
    private ICommand? _removeSocketCommand;
    private ICommand? _moveSocketUpCommand;
    private ICommand? _moveSocketDownCommand;
    private ICommand? _setSocketKindCommand;

    /// <summary>True for a socket of a Group Input / Group Output node: it can be renamed, retyped, moved and removed.</summary>
    public bool IsGroupSocket => Port.Owner is Dyncamelo.Core.Groups.GroupInputNode || Port.Owner is Dyncamelo.Core.Groups.GroupOutputNode;

    /// <summary>Asks for a new name for this group socket.</summary>
    public ICommand RenameSocketCommand => _renameSocketCommand ??= new RelayCommand(() => Node.Owner.RenameGroupSocket(this), () => IsGroupSocket);

    /// <summary>Removes this group socket and the wires on it.</summary>
    public ICommand RemoveSocketCommand => _removeSocketCommand ??= new RelayCommand(() => Node.Owner.RemoveGroupSocket(this), () => IsGroupSocket);

    /// <summary>Moves this group socket one place up.</summary>
    public ICommand MoveSocketUpCommand => _moveSocketUpCommand ??= new RelayCommand(() => Node.Owner.MoveGroupSocket(this, -1), () => IsGroupSocket);

    /// <summary>Moves this group socket one place down.</summary>
    public ICommand MoveSocketDownCommand => _moveSocketDownCommand ??= new RelayCommand(() => Node.Owner.MoveGroupSocket(this, 1), () => IsGroupSocket);

    /// <summary>Sets the kind of this group socket (parameter: "number", "text*", "" for any).</summary>
    public ICommand SetSocketKindCommand => _setSocketKindCommand ??= new RelayCommand<string>(kind => Node.Owner.SetGroupSocketKind(this, kind), _ => IsGroupSocket);

    /// <summary>True when the Player offers this input as a field of a script's form.</summary>
    public bool IsPlayerExposed => Port.PlayerExposed;

    /// <summary>True when this input could be a field of the Player's form: it has an editor and no wire.</summary>
    public bool CanBePlayerField => IsInput && !_isConnected && PlayerExposure.CanOffer(Port);

    private ICommand? _togglePlayerCommand;

    /// <summary>Offers or withdraws this input in the Player (right-click ▸ Show in Player).</summary>
    public ICommand TogglePlayerCommand => _togglePlayerCommand ??= new RelayCommand(() => Node.Owner.TogglePlayerInput(this));

    /// <summary>Owning node view model (null for a field of the Script Player's form).</summary>
    public NodeViewModel Node { get; }

    private IConnectorHost Host => _host ?? Node.Owner;

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

            if (IsMultiInput)
            {
                text += "\naccepts any number of wires (" + _wireCount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        " connected), combined in the order they were made";
            }

            if (Port.Description.Length > 0)
            {
                text += "\n" + Port.Description;
            }

            var current = CurrentValueText();
            if (current != null)
            {
                text += "\n" + current;
            }

            return text;
        }
    }

    // What the socket holds right now: an output's last result, or what the wire into an input delivers. Read when the
    // tooltip is about to show (RefreshToolTip), so it costs nothing while the graph runs.
    private string? CurrentValueText()
    {
        if (!IsInput)
        {
            if (Port.Owner.State == NodeState.Error)
            {
                return "no value — the node failed";
            }

            return Port.Value == null && Port.Owner.State == NodeState.Idle
                ? null
                : "value: " + ValueSummary.Describe(Port.Value);
        }

        var graph = Port.Owner.Graph;
        if (graph == null)
        {
            return null;
        }

        var sources = graph.Connections.Where(c => c.Target == Port && !c.IsMuted).Select(c => c.Source).ToList();
        if (sources.Count == 0)
        {
            return null;
        }

        return sources.Count == 1
            ? "receives: " + ValueSummary.Describe(sources[0].Value)
            : "receives " + sources.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " values, one per wire";
    }

    /// <summary>Re-reads the tooltip (the value line changes with every run); called as the pointer enters the socket.</summary>
    public void RefreshToolTip() => OnPropertyChanged(nameof(ToolTip));

    // ----- type language: colour = family, shape = structure -------------------

    /// <summary>The port's current kind: declared, or refined from its last value / upstream wire when the declaration says "any".</summary>
    public PortKind Kind => _kind;

    /// <summary>Colour family of the socket and of wires leaving it.</summary>
    public PortFamily Family => _kind.Family;

    /// <summary>Structure (item, list, nested list, unknown) — the socket's shape.</summary>
    public PortDepth Depth => _kind.Depth;

    /// <summary>Shared frozen brush for the family.</summary>
    public Brush FamilyBrush => PortBrushes.For(_kind.Family);

    /// <summary>Re-reads the family colour after the palette (and with it the light/dark variants) changed.</summary>
    public void RefreshBrushes() => OnPropertyChanged(nameof(FamilyBrush));

    /// <summary>One letter naming the family, drawn in the socket when the colour-blind aid is on; otherwise empty.</summary>
    public string SocketGlyph => Host.ColourBlindGlyphs ? PortKindPalette.Glyph(_kind.Family) : string.Empty;

    /// <summary>Re-raises <see cref="SocketGlyph"/> after the preference changed.</summary>
    public void RefreshGlyph() => OnPropertyChanged(nameof(SocketGlyph));

    /// <summary>Shared frozen glyph for the structure: 10×10, or a pill as tall as the wires it carries for a multi-input.</summary>
    public Geometry SocketGeometry => IsMultiInput ? PortBrushes.Pill(PillHeight) : PortBrushes.Glyph(_kind.Depth);

    // ----- multi-input: one pill, many wires --------------------------------------------

    /// <summary>Vertical distance, in pixels, between the points where neighbouring wires land on a multi-input pill.</summary>
    public const double SlotSpacing = 9d;

    private int _wireCount;

    /// <summary>True for an input that accepts any number of wires.</summary>
    public bool IsMultiInput => IsInput && Port.IsMultiInput;

    /// <summary>Number of wires feeding this input; maintained by the editor view model.</summary>
    public int WireCount
    {
        get => _wireCount;
        set
        {
            if (SetProperty(ref _wireCount, value))
            {
                OnPropertyChanged(nameof(PillHeight));
                OnPropertyChanged(nameof(SocketHeight));
                OnPropertyChanged(nameof(SocketGeometry));
                OnPropertyChanged(nameof(ToolTip));
                Node?.RefreshRowHeight(this);
            }
        }
    }

    /// <summary>Height of the multi-input pill: room for one landing point per wire, never smaller than an ordinary socket.</summary>
    public double PillHeight => System.Math.Max(14d, (System.Math.Max(_wireCount, 1) - 1) * SlotSpacing + 14d);

    /// <summary>Height of the socket element: the pill for a multi-input, else 14.</summary>
    public double SocketHeight => IsMultiInput ? PillHeight : 14d;

    /// <summary>Vertical offset from the socket's centre of the landing point of wire number <paramref name="index"/> of <paramref name="count"/>.</summary>
    public static double SlotOffset(int index, int count) => count <= 1 ? 0d : (index - (count - 1) / 2d) * SlotSpacing;

    /// <summary>The wire slot nearest to a graph-space Y coordinate (0 when there is at most one wire).</summary>
    public int SlotAt(double graphY)
    {
        if (_wireCount <= 1)
        {
            return 0;
        }

        var slot = (int)System.Math.Round((graphY - _anchor.Y) / SlotSpacing + (_wireCount - 1) / 2d);
        return System.Math.Max(0, System.Math.Min(_wireCount - 1, slot));
    }

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
        if (IsMultiInput)
        {
            return;     // several wires may disagree; a multi-input keeps its declared kind
        }

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
        OnPropertyChanged(nameof(SocketGlyph));
        OnPropertyChanged(nameof(SocketGeometry));
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(ToolTip));
    }

    // ----- inline editors (unwired inputs edit their own value) ------------------

    /// <summary>The inline editor this input gets while unwired (none for wire-only types).</summary>
    public PortEditorKind EditorKind { get; private set; }

    /// <summary>Range, step and unit of a number editor; null for other editors.</summary>
    public NumberEditSpec? NumberSpec { get; private set; }

    /// <summary>True while an editor should be shown: the input has one and no wire feeds it.</summary>
    public bool ShowEditor => EditorKind != PortEditorKind.None && !_isConnected;

    /// <summary>True when the label is drawn by the row (number fields draw their own label inside the field).</summary>
    public bool ShowPlainLabel => !(ShowEditor && EditorKind == PortEditorKind.Number);

    /// <summary>Hard minimum of the number editor.</summary>
    public double NumberMin => NumberSpec?.Min ?? double.MinValue;

    /// <summary>Hard maximum of the number editor.</summary>
    public double NumberMax => NumberSpec?.Max ?? double.MaxValue;

    /// <summary>Slider extent minimum (NaN when there is no finite range).</summary>
    public double NumberSoftMin => NumberSpec?.SoftMin ?? double.NaN;

    /// <summary>Slider extent maximum (NaN when there is no finite range).</summary>
    public double NumberSoftMax => NumberSpec?.SoftMax ?? double.NaN;

    /// <summary>Increment of the number editor.</summary>
    public double NumberStep => NumberSpec?.Step ?? 1d;

    /// <summary>True when the number editor is integer-only.</summary>
    public bool NumberIsInteger => NumberSpec?.IsInteger ?? false;

    /// <summary>Unit suffix of the number editor.</summary>
    public string NumberUnit => NumberSpec?.Unit ?? string.Empty;

    /// <summary>The number shown by the number editor; setting it pins the value (equal to the default clears the pin).</summary>
    public double NumberValue
    {
        get => PortEditors.GetNumber(Port);
        set => PortEditors.SetNumber(Port, value);
    }

    /// <summary>
    /// Pastes text holding several numbers ("1, 2, 3") into this field and the number fields after it, as one undo step.
    /// </summary>
    /// <param name="text">The clipboard text.</param>
    /// <returns>True when two or more fields were filled (a single number is left to the ordinary paste).</returns>
    public bool TryPasteNumbers(string? text)
    {
        var owner = Host;
        var inputs = Port.Owner.InPorts;
        var graph = Port.Owner.Graph;
        int filled;
        using (owner.History.Begin("Paste values"))
        {
            var index = -1;
            for (var i = 0; i < inputs.Count; i++)
            {
                if (ReferenceEquals(inputs[i], Port))
                {
                    index = i;
                    break;
                }
            }

            filled = PortEditors.PasteNumbers(inputs, index, text, p => graph != null && graph.FindConnectionInto(p) != null);
        }

        if (filled > 0)
        {
            owner.ReportStatus("Pasted " + filled.ToString(System.Globalization.CultureInfo.InvariantCulture) + " values from " + Title + " on.");
        }

        return filled > 0;
    }

    /// <summary>The boolean shown by the toggle editor.</summary>
    public bool BoolValue
    {
        get => PortEditors.GetBool(Port);
        set => PortEditors.SetBool(Port, value);
    }

    /// <summary>The text shown by the text and path editors.</summary>
    public string TextValue
    {
        get => PortEditors.GetText(Port);
        set => PortEditors.SetText(Port, value);
    }

    /// <summary>The colour as #AARRGGBB, or empty when unset.</summary>
    public string ColourHex
    {
        get => PortEditors.GetColourHex(Port);
        set
        {
            if (PortEditors.TryParseHex(value, out var a, out var r, out var g, out var b))
            {
                PortEditors.SetColour(Port, a, r, g, b);
            }
        }
    }

    /// <summary>Stored model elements as text ("Pipe-101 (+2)"); empty when nothing is picked.</summary>
    public string ModelSummary
    {
        get
        {
            var stored = PortEditors.Current(Port) as string;
            var count = ModelPickerHost.CountOf(stored);
            if (count == 0)
            {
                return string.Empty;
            }

            var described = ModelPickerHost.Current?.Describe(stored);
            return string.IsNullOrEmpty(described)
                ? count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " element(s)"
                : described!;
        }
    }

    /// <summary>True when model elements are picked on this input.</summary>
    public bool HasModelValue => ModelPickerHost.CountOf(PortEditors.Current(Port) as string) > 0;

    /// <summary>Takes the host's current selection as this input's value.</summary>
    public ICommand CaptureModelCommand { get; }

    /// <summary>Selects the picked elements in the host again.</summary>
    public ICommand RevealModelCommand { get; }

    private void CaptureModel()
    {
        var picker = ModelPickerHost.Current;
        if (picker == null)
        {
            return;
        }

        var single = PortEditors.IsSingleModelItem(Port);
        var value = picker.CaptureSelection(single, out var count);
        if (value == null)
        {
            Host.ReportProblem("Select something in Navisworks first, then use the picker.");
            return;
        }

        Port.SetUserValue(value);
        Host.ReportProblem(single && count > 1
            ? "'" + Port.Name + "' takes one element: used the first of " + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " selected."
            : "'" + Port.Name + "': picked " + Math.Max(count, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " element(s).");
    }

    private void RevealModel()
    {
        var stored = PortEditors.Current(Port) as string;
        if (ModelPickerHost.Current?.Reveal(stored) != true)
        {
            Host.ReportProblem("The picked elements are no longer in the model.");
        }
    }

    /// <summary>The colour as a brush for the swatch (transparent when unset).</summary>
    public Brush ColourBrush
    {
        get
        {
            if (PortEditors.TryParseHex(PortEditors.GetColourHex(Port), out var a, out var r, out var g, out var b))
            {
                var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
                brush.Freeze();
                return brush;
            }

            return Brushes.Transparent;
        }
    }

    /// <summary>True when the input carries a pinned value (shown with a marker; reset returns to the default).</summary>
    public bool IsModified => Port.HasUserValue;

    /// <summary>True when the input has neither a pinned value nor a default (the field shows a placeholder).</summary>
    public bool IsUnset => PortEditors.IsUnset(Port);

    /// <summary>True for a drop-down choice editor.</summary>
    public bool ShowDropdownChoices => ShowChoiceEditor && !PortEditors.UseSegmentedChoices(Port.Choices);

    /// <summary>True for a segmented-button choice editor (few, short options).</summary>
    public bool ShowSegmentedChoices => ShowChoiceEditor && PortEditors.UseSegmentedChoices(Port.Choices);

    /// <summary>Returns the input to its default (clears the pinned value).</summary>
    public ICommand ResetCommand { get; }

    /// <summary>Opens a file (or folder) chooser for a path input.</summary>
    public ICommand BrowseCommand { get; }

    private void BrowsePath()
    {
        var dialogs = Host.Dialogs;
        string? chosen;
        if (PortEditors.IsFolder(Port))
        {
            chosen = dialogs.PickFolder("Choose folder for '" + Port.Name + "'", PortEditors.GetText(Port));
        }
        else
        {
            var name = Port.Name.ToLowerInvariant();
            var isOutput = name.Contains("output") || name.Contains("save") || name.Contains("export") ||
                           name.Contains("target") || name.Contains("destination");
            chosen = isOutput
                ? dialogs.ShowSaveFile("All files (*.*)|*.*", "Choose file for '" + Port.Name + "'", PortEditors.GetText(Port))
                : dialogs.ShowOpenFile("All files (*.*)|*.*", "Choose file for '" + Port.Name + "'");
        }

        if (!string.IsNullOrEmpty(chosen))
        {
            PortEditors.SetText(Port, chosen);
        }
    }

    private void RaiseValueChanged()
    {
        OnPropertyChanged(nameof(SelectedChoice));
        OnPropertyChanged(nameof(NumberValue));
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(TextValue));
        OnPropertyChanged(nameof(ColourHex));
        OnPropertyChanged(nameof(ColourBrush));
        OnPropertyChanged(nameof(ModelSummary));
        OnPropertyChanged(nameof(HasModelValue));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(IsUnset));
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
                // A wire hides the inline editor and vice-versa.
                OnPropertyChanged(nameof(ShowChoiceEditor));
                OnPropertyChanged(nameof(ShowDropdownChoices));
                OnPropertyChanged(nameof(ShowSegmentedChoices));
                OnPropertyChanged(nameof(ShowEditor));
                OnPropertyChanged(nameof(ShowPlainLabel));
                OnPropertyChanged(nameof(CanBePlayerField));
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
