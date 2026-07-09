# Nodify 7.3.0 Integration Cookbook (net48, WPF) — for Dyncamelo.UI

**Ground truth:** every type/member below was verified against `lib/net48/Nodify.xml` + DLL metadata of the exact NuGet package `nodify 7.3.0` (downloaded from nuget.org; extracted to scratchpad). Patterns cross-checked against the miroiu/nodify wiki and the official `Examples/Nodify.Calculator` app. The package ships `lib/net48/Nodify.dll` — net48 is a first-class target.

**Verified class hierarchy (from DLL metadata):**
- `Nodify.NodifyEditor : System.Windows.Controls.Primitives.MultiSelector` (so `ItemsSource`, `ItemTemplate`, `ItemContainerStyle` are the normal ItemsControl members)
- `Nodify.ItemContainer : ContentControl` — generated container for each item in `ItemsSource`
- `Nodify.Connector : Control`; `NodeInput : Connector`; `NodeOutput : Connector`; `StateNode : Connector`; `KnotNode : ContentControl` (owns a Connector)
- `Nodify.Node : HeaderedContentControl`; `GroupingNode : HeaderedContentControl`
- `Nodify.BaseConnection : Shape`; `Connection : BaseConnection` (cubic bezier); `LineConnection : BaseConnection`; `StepConnection : LineConnection`; `CircuitConnection : LineConnection`
- `Nodify.PendingConnection : ContentControl`
- `Nodify.ConnectionsMultiSelector : MultiSelector` (internal host for `Connections`); `ConnectionContainer : ContentPresenter` (per-connection container, has `IsSelectable`/`IsSelected`)
- `Nodify.Minimap : ItemsControl`; `MinimapItem : ContentControl`
- `Nodify.DecoratorsControl : ItemsControl`; `DecoratorContainer : ContentControl` (has `Location`)

XAML namespace (XmlnsDefinition verified in DLL): `xmlns:nodify="https://miroiu.github.io/nodify"`.

---

## 1. NodifyEditor setup XAML

Three data-bound layers: **items** (`ItemsSource` → `ItemContainer`s), **connections** (`Connections` → `ConnectionContainer`s via `ConnectionTemplate`), **decorators** (`Decorators` → `DecoratorContainer`s via `DecoratorTemplate`, for in-canvas overlays like a node-search popup).

```xml
<nodify:NodifyEditor x:Name="Editor"
                     ItemsSource="{Binding Nodes}"
                     Connections="{Binding Connections}"
                     SelectedItems="{Binding SelectedNodes}"
                     ViewportZoom="{Binding Zoom, Mode=TwoWay}"
                     ViewportLocation="{Binding ViewportLocation, Mode=TwoWay}"
                     PendingConnection="{Binding PendingConnection}"
                     PendingConnectionTemplate="{StaticResource PendingConnectionTemplate}"
                     ConnectionTemplate="{StaticResource ConnectionTemplate}"
                     DisconnectConnectorCommand="{Binding DisconnectConnectorCommand}"
                     RemoveConnectionCommand="{Binding RemoveConnectionCommand}"
                     ItemContainerStyle="{StaticResource NodeContainerStyle}"
                     GridCellSize="15">
    <nodify:NodifyEditor.ItemTemplate>
        <DataTemplate DataType="{x:Type vm:NodeViewModel}">
            <nodify:Node Header="{Binding Title}"
                         Input="{Binding Inputs}"
                         Output="{Binding Outputs}" />
        </DataTemplate>
    </nodify:NodifyEditor.ItemTemplate>
</nodify:NodifyEditor>
```

Key facts (all verified members):
- `NodifyEditor.Connections` — "the data source that `BaseConnection`s will be generated for". Plain `IEnumerable` DP; use `ObservableCollection<ConnectionViewModel>`.
- `NodifyEditor.PendingConnection` — sets the **DataContext** of the single `PendingConnection` control.
- Node position: bind `ItemContainer.Location` (type `Point`, graph-space) in `ItemContainerStyle` (see §5).
- `nodify:Node` is a `HeaderedContentControl` with extra DPs: `Input`, `Output` (each an `IEnumerable` rendered as `NodeInput`/`NodeOutput` controls), `InputConnectorTemplate`, `OutputConnectorTemplate`, `Footer`, `FooterTemplate`, `HasFooter`, `ContentBrush`, `HeaderBrush`, `FooterBrush`, `ContentPadding`, `ContentContainerStyle`, `HeaderContainerStyle`, `FooterContainerStyle`.
- Connector wiring: `Connector.Anchor` (Point, graph-space) must be bound `OneWayToSource` into the VM; `Connector.IsConnected` must be true or the connector stops publishing `Anchor` updates when the node moves/resizes. The canonical approach (from the official Calculator example) is an implicit style scoped to the editor:

```xml
<nodify:NodifyEditor.Resources>
    <Style TargetType="{x:Type nodify:NodeInput}"
           BasedOn="{StaticResource {x:Type nodify:NodeInput}}">
        <Setter Property="Header" Value="{Binding}" />
        <Setter Property="IsConnected" Value="{Binding IsConnected}" />
        <Setter Property="Anchor" Value="{Binding Anchor, Mode=OneWayToSource}" />
        <Setter Property="HeaderTemplate">
            <Setter.Value>
                <DataTemplate DataType="{x:Type vm:ConnectorViewModel}">
                    <TextBlock Text="{Binding Title}" />
                </DataTemplate>
            </Setter.Value>
        </Setter>
    </Style>
    <Style TargetType="{x:Type nodify:NodeOutput}"
           BasedOn="{StaticResource {x:Type nodify:NodeOutput}}">
        <Setter Property="Header" Value="{Binding}" />
        <Setter Property="IsConnected" Value="{Binding IsConnected}" />
        <Setter Property="Anchor" Value="{Binding Anchor, Mode=OneWayToSource}" />
    </Style>
</nodify:NodifyEditor.Resources>
```

`NodeInput`/`NodeOutput` own DPs: `Header`, `HeaderTemplate`, `ConnectorTemplate`, `Orientation`. The visual "dot" is template part `PART_Connector` (const `Connector.ElementConnector`); the anchor is computed from its center. For Dynamo-like typed ports you can restyle `ConnectorTemplate` per data type via a `Style`/`DataTrigger` on the connector VM.

## 2. Pending connection pattern

Two alternative command surfaces (mutually exclusive — the editor-level ones win):
- **Editor-level:** `NodifyEditor.ConnectionStartedCommand` (parameter = `PendingConnection.Source`, i.e. the source connector's DataContext) and `NodifyEditor.ConnectionCompletedCommand` (parameter = `Tuple<object, object>` of Source and Target DataContexts).
- **Template-level (recommended, used by official examples):** `PendingConnection.StartedCommand` (parameter = Source VM) and `PendingConnection.CompletedCommand` (parameter = Target VM), combined with `OneWayToSource` bindings of `Source`/`Target` into a `PendingConnectionViewModel`, so the VM owns all state:

```xml
<DataTemplate x:Key="PendingConnectionTemplate" DataType="{x:Type vm:PendingConnectionViewModel}">
    <nodify:PendingConnection IsVisible="{Binding IsVisible}"
                              Source="{Binding Source, Mode=OneWayToSource}"
                              Target="{Binding Target, Mode=OneWayToSource}"
                              TargetAnchor="{Binding TargetLocation, Mode=OneWayToSource}"
                              EnablePreview="True"
                              PreviewTarget="{Binding PreviewTarget, Mode=OneWayToSource}"
                              EnableSnapping="True"
                              AllowOnlyConnectors="True"
                              StartedCommand="{Binding DataContext.StartConnectionCommand,
                                  RelativeSource={RelativeSource AncestorType={x:Type nodify:NodifyEditor}}}"
                              CompletedCommand="{Binding DataContext.CreateConnectionCommand,
                                  RelativeSource={RelativeSource AncestorType={x:Type nodify:NodifyEditor}}}" />
</DataTemplate>
```

Semantics (verified from XML docs):
- Drag starts from a `Connector` (default gesture LeftClick-drag); `PendingConnection.Source` = source connector's DataContext; `Target` is set **only on completion** (null when dropped on empty canvas — use that to open a Dynamo-style "connect to new node" popup).
- `EnablePreview` + `PreviewTarget` give live hover feedback (requires `EnableHitTesting`, default on); `EnableSnapping` snaps `TargetAnchor` to the hovered connector; `AllowOnlyConnectors=true` restricts targets to `Connector`s (false also allows dropping on `ItemContainer`s).
- Attached bool `PendingConnection.IsOverElement` is set on hovered connectors/containers — usable in connector styles to highlight legal drop targets.
- `StartedCommand.CanExecute(source)` returning false blocks starting a drag (e.g. forbid dragging from an already-connected input, the Calculator does exactly this); `CompletedCommand.CanExecute` gates creation, so **type-compatibility validation lives in the VM**.
- Direction: `PendingConnection.Direction` (`ConnectionDirection.Forward/Backward`). Source/target resolution is purely DataContext-based — in the VM you receive your own `ConnectorViewModel`s; normalize output→input yourself (see §7).

## 3. Connections

`ConnectionTemplate` (a `DataTemplate` on the editor) instantiates one shape per item in `Connections`:

```xml
<DataTemplate x:Key="ConnectionTemplate" DataType="{x:Type vm:ConnectionViewModel}">
    <nodify:Connection Source="{Binding Source.Anchor}"
                       Target="{Binding Target.Anchor}"
                       SourceOffset="5 0" TargetOffset="5 0"
                       SourceOffsetMode="Edge" TargetOffsetMode="Edge"
                       Direction="Forward" />
</DataTemplate>
```

- All connection shapes derive from `BaseConnection : Shape` with `Source`/`Target` **of type `Point`** — you bind them to the connectors' `Anchor` values; they update live as nodes move.
- Types: `Connection` (cubic bezier — the Dynamo look), `LineConnection` (straight, `CornerRadius`), `StepConnection` (orthogonal; `SourcePosition`/`TargetPosition` of enum `ConnectorPosition {Top, Left, Bottom, Right}`), `CircuitConnection` (`Angle` in degrees).
- Useful `BaseConnection` DPs: `SourceOffset`/`TargetOffset` + `SourceOffsetMode`/`TargetOffsetMode` (`ConnectionOffsetMode {None, Circle, Rectangle, Edge, Static}`), `Spacing`, `ArrowSize`, `ArrowEnds {Start, End, Both, None}`, `ArrowShape {Arrowhead, Ellipse, Rectangle}`, `DirectionalArrowsCount` + `IsAnimatingDirectionalArrows` + `StartAnimation(double)`/`StopAnimation` (nice for "graph is running" feedback), `OutlineBrush`/`OutlineThickness`, `Text` + font properties (label drawn on the wire).
- Removal: `BaseConnection.DisconnectCommand` (parameter = disconnect location) or, simpler, editor-level `NodifyEditor.RemoveConnectionCommand` — invoked when `BaseConnection.Disconnect` event fires (default gesture **Alt+LeftClick** on the wire), **parameter = the connection's DataContext** (your `ConnectionViewModel`). `SplitCommand`/`Split` event (default LeftDoubleClick) supports reroute/knot workflows (pair with `KnotNode`).
- Selection of wires: `ConnectionContainer.IsSelectable/IsSelected`; editor exposes `SelectedConnection`, `SelectedConnections`, `CanSelectMultipleConnections`, `SelectAllConnections()`/`UnselectAllConnections()`.
- `DisplayConnectionsOnTop` draws the connection layer above nodes (default false).

## 4. Viewport: zoom/pan, fit, minimap

- DPs: `ViewportZoom` (double, two-way bindable), `ViewportLocation` (Point, top-left in graph space, two-way bindable), `ViewportSize` (read), `ViewportTransform` (bind grid `DrawingBrush.Transform` to it for a panning-aware background grid — official pattern), `MinViewportZoom`, `MaxViewportZoom`, `DisableZooming`, `DisablePanning`, `DisableAutoPanning`, `AutoPanSpeed`, `AutoPanEdgeDistance`.
- Defaults (verified gesture docs): pan = **RightClick or MiddleClick drag**; zoom = mouse wheel with no modifier (`ZoomModifierKey` default `None`); auto-pan when dragging near edges is on.
- Methods: `ZoomIn()`, `ZoomOut()`, `ZoomAtPosition(double zoom, Point location)`, `BringIntoView(Point, bool animated, Action onFinish)`, `BringIntoView(Rect)`, `ResetViewport(bool animated, Action onFinish)` (to (0,0) zoom 1), `FitToScreen(Rect? area = null)` (fits all items when null; `FitToScreenExtentMargin` pads it). `ItemsExtent` = rect covered by all containers. `GetLocationInsideEditor(MouseEventArgs|DragEventArgs|Point,UIElement)` converts to graph space (use for drag-drop node creation). Events: `ViewportUpdated`.
- Routed commands in `EditorCommands` (bind straight from toolbar buttons with `CommandTarget="{Binding ElementName=Editor}"`): `ZoomIn`, `ZoomOut`, `SelectAll`, `BringIntoView` (param Point-or-string), `FitToScreen`, `Align` (param `Nodify.Alignment {Top, Left, Bottom, Right, Middle, Center}` or string), `LockSelection`, `UnlockSelection`.
- **Minimap exists in 7.3.0** (`Nodify.Minimap : ItemsControl`). Canonical wiring (wiki-verified):

```xml
<nodify:Minimap ItemsSource="{Binding ItemsSource, ElementName=Editor}"
                ViewportLocation="{Binding ViewportLocation, ElementName=Editor}"
                ViewportSize="{Binding ViewportSize, ElementName=Editor}"
                Zoom="OnMinimapZoom">
    <nodify:Minimap.ItemContainerStyle>
        <Style TargetType="nodify:MinimapItem">
            <Setter Property="Location" Value="{Binding Location}" />
        </Style>
    </nodify:Minimap.ItemContainerStyle>
</nodify:Minimap>
```
```csharp
private void OnMinimapZoom(object sender, ZoomEventArgs e) => Editor.ZoomAtPosition(e.Zoom, e.Location);
```
Other Minimap members: `Extent`, `ItemsExtent`, `MaxViewportOffset`, `ResizeToViewport`, `IsReadOnly`, `ViewportStyle`.

## 5. Selection, dragging, Location, GridSnap, disconnect/remove gestures

- **ItemContainer** DPs: `Location` (two-way; raises `LocationChanged`/`PreviewLocationChanged`), `IsSelected` (two-way bindable), `IsSelectable`, `IsDraggable`, `ActualSize` (bind `OneWayToSource` to capture node size for .dyc persistence), `IsPreviewingSelection`, `IsPreviewingLocation`, `SelectedBrush`, `HighlightBrush`, `SelectedBorderThickness`, `SelectedMargin`. Canonical style:

```xml
<Style x:Key="NodeContainerStyle" TargetType="{x:Type nodify:ItemContainer}"
       BasedOn="{StaticResource {x:Type nodify:ItemContainer}}">
    <Setter Property="Location" Value="{Binding Location}" />
    <Setter Property="IsSelected" Value="{Binding IsSelected}" />
    <Setter Property="ActualSize" Value="{Binding Size, Mode=OneWayToSource}" />
</Style>
```
- **Selection**: editor `SelectedItems` (bind an `IList` VM collection — the Calculator binds `SelectedItems="{Binding SelectedOperations}"` and it stays in sync both ways), `SelectedContainers`, `SelectedContainersCount`, `CanSelectMultipleItems`, `EnableRealtimeSelection` (rubber-band updates while dragging; on by default via theme), `SelectionRectangleStyle`, `IsSelecting`, `SelectedArea`; methods `Select(container)`, `SelectArea(rect)`, `UnselectArea(rect)`, `InvertSelection(rect)`; commands `ItemsSelectStartedCommand`/`ItemsSelectCompletedCommand`. Default gestures: LeftClick/rubber-band replace; modifier-based append/remove/invert strategies exist (`EditorGestures.SelectionGestures` has `Replace`, `Remove`, `Append`, `Invert`).
- **Dragging**: built-in; multi-drag of selection; `ItemsDragStartedCommand`/`ItemsDragCompletedCommand` (fire around a container drag — hook these for undo/redo recording), `ItemsMoved` event (`ItemsMovedEventArgs.Items` + `.Offset`), `IsDragging`.
- **Grid snap**: `GridCellSize` (int cell size; containers snap while dragging) + `EnableSnappingCorrection` (re-snap on drop if start position was off-grid) + `SnapToGrid(double)` helper. Draw the visible grid yourself with a `DrawingBrush` background whose `Transform` binds to `ViewportTransform` (Viewport="0 0 15 15" matching `GridCellSize`).
- **Disconnect/remove gestures** (defaults, all remappable): connector Alt+LeftClick **or Delete (focused)** → `Connector.Disconnect` event → `NodifyEditor.DisconnectConnectorCommand` (param = connector DataContext); wire Alt+LeftClick → `BaseConnection.Disconnect` → `NodifyEditor.RemoveConnectionCommand` (param = connection DataContext); wire LeftDoubleClick → `Split`. **There is no built-in "delete node" gesture** — add `<KeyBinding Key="Delete" Command="{Binding DeleteSelectionCommand}"/>` in `NodifyEditor.InputBindings` (official pattern).
- All gestures remappable at startup via the static `Nodify.Interactivity.EditorGestures.Mappings` (properties `Editor`, `ItemContainer`, `Connector`, `Connection`, `GroupingNode`, `Minimap`; each has `.Apply(...)`/`.Unbind()`), e.g. `EditorGestures.Mappings.Editor.Pan.Value = new MouseGesture(MouseAction.MiddleClick);`.

## 6. Styling/theming + net48 quirks

- Default styles ship in the DLL (`Themes/Generic.xaml` merges `Themes/Styles/Controls.xaml`), so controls render with the dark default **with no theme merged at all**. Full themes (verified BAML resources in the 7.3.0 net48 DLL): merge exactly one of

```xml
<ResourceDictionary Source="pack://application:,,,/Nodify;component/Themes/Dark.xaml" />
<!-- or Themes/Light.xaml, or Themes/Nodify.xaml -->
```

- Theme structure: `Dark/Light/Nodify.xaml` define `Color` resources keyed per control (`NodifyEditor.BackgroundColor`, `Node.HeaderColor`, `ItemContainer.SelectedColor`, `Connector.BorderColor`, ...) and merge `Themes/Controls.xaml` (implicit styles) ← `Themes/Brushes.xaml` (brushes from those colors, e.g. `NodifyEditor.SelectionRectangleStrokeBrush`). To recolor for a Dyncamelo brand, merge Dark.xaml then override the `Color`/brush keys in a dictionary merged **after** it. To restyle a control, use `BasedOn="{StaticResource {x:Type nodify:Node}}"` etc.
- **Navisworks add-in quirk (critical):** Dyncamelo.App has no `App.xaml` of its own — `Application.Current` belongs to (or may not exist in) the Navisworks process. Merge the Nodify theme into the **root UserControl's `Resources`** of the dock pane instead of application resources; if `Application.Current` exists, merging into `Application.Current.Resources` from plugin startup also works but risks fighting other add-ins. Per-control `DynamicResource` lookups resolve fine through the element tree.
- Never wrap `NodifyEditor` in a `ScrollViewer` — it manages its own viewport transform (`TranslateTransform`/`ScaleTransform` fields).
- Perf knobs for big graphs: `EnableRenderingContainersOptimizations`, `OptimizeRenderingMinimumContainers`, `OptimizeRenderingZoomOutPercent`, `EnableDraggingContainersOptimizations` (set **false** if you want live minimap/wire updates while dragging), static `Connector.EnableOptimizations`, `PendingConnection.EnableHitTesting=false` for huge graphs, `IsBulkUpdatingItems` when loading a .dyc.
- net48-specific: none beyond WPF baseline — the net48 assembly is identical API-wise (XML docs match). C# 10 + `LangVersion 10` is fine since Nodify is consumed via XAML/DPs. `xmlns:nodify="https://miroiu.github.io/nodify"` works via `XmlnsDefinitionAttribute` (verified). The Calculator example's `xmlns:sys="clr-namespace:System;assembly=System.Runtime"` must be `assembly=mscorlib` on net48.

## 7. Minimal complete MVVM sketch

VMs (assumes an `ObservableObject` base with `SetProperty` and a `RelayCommand`/`RelayCommand<T>`; classic classes, no records):

```csharp
public class ConnectorViewModel : ObservableObject
{
    private Point _anchor;               // written by Nodify via OneWayToSource
    private bool _isConnected;
    public string Title { get; set; } = string.Empty;
    public bool IsInput { get; set; }
    public NodeViewModel Node { get; set; } = null!;   // back-reference
    public Point Anchor { get => _anchor; set => SetProperty(ref _anchor, value); }
    public bool IsConnected { get => _isConnected; set => SetProperty(ref _isConnected, value); }
}

public class NodeViewModel : ObservableObject
{
    private Point _location;
    public string Title { get; set; } = string.Empty;
    public Point Location { get => _location; set => SetProperty(ref _location, value); }
    public ObservableCollection<ConnectorViewModel> Inputs { get; } = new ObservableCollection<ConnectorViewModel>();
    public ObservableCollection<ConnectorViewModel> Outputs { get; } = new ObservableCollection<ConnectorViewModel>();
}

public class ConnectionViewModel : ObservableObject
{
    public ConnectionViewModel(ConnectorViewModel source, ConnectorViewModel target)
    { Source = source; Target = target; }
    public ConnectorViewModel Source { get; }   // an output
    public ConnectorViewModel Target { get; }   // an input
}

public class PendingConnectionViewModel : ObservableObject
{
    private ConnectorViewModel? _source; private ConnectorViewModel? _target;
    private bool _isVisible; private Point _targetLocation;
    public ConnectorViewModel? Source { get => _source; set => SetProperty(ref _source, value); }
    public ConnectorViewModel? Target { get => _target; set => SetProperty(ref _target, value); }
    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }
    public Point TargetLocation { get => _targetLocation; set => SetProperty(ref _targetLocation, value); }
}

public class EditorViewModel : ObservableObject
{
    public ObservableCollection<NodeViewModel> Nodes { get; } = new ObservableCollection<NodeViewModel>();
    public ObservableCollection<ConnectionViewModel> Connections { get; } = new ObservableCollection<ConnectionViewModel>();
    public ObservableCollection<NodeViewModel> SelectedNodes { get; } = new ObservableCollection<NodeViewModel>();
    public PendingConnectionViewModel PendingConnection { get; } = new PendingConnectionViewModel();

    public ICommand StartConnectionCommand { get; }
    public ICommand CreateConnectionCommand { get; }
    public ICommand DisconnectConnectorCommand { get; }
    public ICommand RemoveConnectionCommand { get; }
    public ICommand DeleteSelectionCommand { get; }
    public ICommand AddNodeCommand { get; }

    public EditorViewModel()
    {
        StartConnectionCommand = new RelayCommand<ConnectorViewModel>(
            _ => PendingConnection.IsVisible = true,
            c => c != null && !(c.IsInput && c.IsConnected));      // block dragging from occupied inputs

        CreateConnectionCommand = new RelayCommand<ConnectorViewModel>(
            _ => { PendingConnection.IsVisible = false; TryConnect(PendingConnection.Source, PendingConnection.Target); },
            _ => CanConnect(PendingConnection.Source, PendingConnection.Target));

        DisconnectConnectorCommand = new RelayCommand<ConnectorViewModel>(c =>
        {
            foreach (var con in Connections.Where(x => x.Source == c || x.Target == c).ToList())
                RemoveConnection(con);
        });

        RemoveConnectionCommand = new RelayCommand<ConnectionViewModel>(RemoveConnection);

        DeleteSelectionCommand = new RelayCommand(() =>
        {
            foreach (var node in SelectedNodes.ToList())
            {
                foreach (var con in Connections.Where(x => x.Source.Node == node || x.Target.Node == node).ToList())
                    RemoveConnection(con);
                Nodes.Remove(node);
            }
        });

        AddNodeCommand = new RelayCommand<Point>(location =>
            Nodes.Add(NodeFactory.Create("Math.Add", location)));   // location = graph-space point
    }

    private bool CanConnect(ConnectorViewModel? s, ConnectorViewModel? t)
    {
        if (s == null || t == null || s == t || s.Node == t.Node || s.IsInput == t.IsInput) return false;
        var input = s.IsInput ? s : t;
        return !Connections.Any(c => c.Target == input);           // single wire per input (Dynamo rule)
        // + engine-level port type compatibility check goes here
    }

    private void TryConnect(ConnectorViewModel? s, ConnectorViewModel? t)
    {
        if (!CanConnect(s, t)) return;
        var source = s!.IsInput ? t! : s;                          // normalize: Source=output, Target=input
        var target = s.IsInput ? s : t!;
        Connections.Add(new ConnectionViewModel(source, target));
        source.IsConnected = true; target.IsConnected = true;
        // -> notify Dyncamelo.Core engine: edge added, mark target node dirty
    }

    private void RemoveConnection(ConnectionViewModel con)
    {
        Connections.Remove(con);
        con.Source.IsConnected = Connections.Any(c => c.Source == con.Source || c.Target == con.Source);
        con.Target.IsConnected = Connections.Any(c => c.Source == con.Target || c.Target == con.Target);
        // -> notify engine: edge removed
    }
}
```

Full XAML (combining §1–§3 + context menu; `DataContext = EditorViewModel`):

```xml
<UserControl.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="pack://application:,,,/Nodify;component/Themes/Dark.xaml" />
        </ResourceDictionary.MergedDictionaries>

        <DataTemplate x:Key="ConnectionTemplate" DataType="{x:Type vm:ConnectionViewModel}">
            <nodify:Connection Source="{Binding Source.Anchor}" Target="{Binding Target.Anchor}"
                               SourceOffsetMode="Edge" TargetOffsetMode="Edge" />
        </DataTemplate>

        <DataTemplate x:Key="PendingConnectionTemplate" DataType="{x:Type vm:PendingConnectionViewModel}">
            <nodify:PendingConnection IsVisible="{Binding IsVisible}"
                                      Source="{Binding Source, Mode=OneWayToSource}"
                                      Target="{Binding Target, Mode=OneWayToSource}"
                                      TargetAnchor="{Binding TargetLocation, Mode=OneWayToSource}"
                                      EnableSnapping="True"
                                      StartedCommand="{Binding DataContext.StartConnectionCommand,
                                          RelativeSource={RelativeSource AncestorType={x:Type nodify:NodifyEditor}}}"
                                      CompletedCommand="{Binding DataContext.CreateConnectionCommand,
                                          RelativeSource={RelativeSource AncestorType={x:Type nodify:NodifyEditor}}}" />
        </DataTemplate>

        <Style x:Key="NodeContainerStyle" TargetType="{x:Type nodify:ItemContainer}"
               BasedOn="{StaticResource {x:Type nodify:ItemContainer}}">
            <Setter Property="Location" Value="{Binding Location}" />
        </Style>
    </ResourceDictionary>
</UserControl.Resources>

<nodify:NodifyEditor x:Name="Editor"
                     ItemsSource="{Binding Nodes}"
                     Connections="{Binding Connections}"
                     SelectedItems="{Binding SelectedNodes}"
                     PendingConnection="{Binding PendingConnection}"
                     PendingConnectionTemplate="{StaticResource PendingConnectionTemplate}"
                     ConnectionTemplate="{StaticResource ConnectionTemplate}"
                     DisconnectConnectorCommand="{Binding DisconnectConnectorCommand}"
                     RemoveConnectionCommand="{Binding RemoveConnectionCommand}"
                     ItemContainerStyle="{StaticResource NodeContainerStyle}"
                     GridCellSize="15">

    <nodify:NodifyEditor.InputBindings>
        <KeyBinding Key="Delete" Command="{Binding DeleteSelectionCommand}" />
    </nodify:NodifyEditor.InputBindings>

    <nodify:NodifyEditor.ContextMenu>
        <ContextMenu>
            <MenuItem Header="Add node"
                      Command="{Binding PlacementTarget.DataContext.AddNodeCommand,
                          RelativeSource={RelativeSource AncestorType=ContextMenu}}"
                      CommandParameter="{Binding PlacementTarget.MouseLocation,
                          RelativeSource={RelativeSource AncestorType=ContextMenu}}" />
        </ContextMenu>
    </nodify:NodifyEditor.ContextMenu>

    <nodify:NodifyEditor.Resources>
        <Style TargetType="{x:Type nodify:NodeInput}" BasedOn="{StaticResource {x:Type nodify:NodeInput}}">
            <Setter Property="Header" Value="{Binding Title}" />
            <Setter Property="IsConnected" Value="{Binding IsConnected}" />
            <Setter Property="Anchor" Value="{Binding Anchor, Mode=OneWayToSource}" />
        </Style>
        <Style TargetType="{x:Type nodify:NodeOutput}" BasedOn="{StaticResource {x:Type nodify:NodeOutput}}">
            <Setter Property="Header" Value="{Binding Title}" />
            <Setter Property="IsConnected" Value="{Binding IsConnected}" />
            <Setter Property="Anchor" Value="{Binding Anchor, Mode=OneWayToSource}" />
        </Style>
    </nodify:NodifyEditor.Resources>

    <nodify:NodifyEditor.ItemTemplate>
        <DataTemplate DataType="{x:Type vm:NodeViewModel}">
            <nodify:Node Header="{Binding Title}" Input="{Binding Inputs}" Output="{Binding Outputs}" />
        </DataTemplate>
    </nodify:NodifyEditor.ItemTemplate>
</nodify:NodifyEditor>
```

Notes on the sketch:
- `MouseLocation` is a read-only DP on `NodifyEditor` in **graph-space coordinates** — exactly what `AddNodeCommand` needs for placing the node under the cursor; the `PlacementTarget` indirection is required because `ContextMenu` is outside the visual tree. The heavier official alternative is `HasCustomContextMenu="True"` + a `DecoratorContainer` hosting a search popup (Dynamo-style node search) whose `Location` you set from `MouseLocation`; decorators live in graph space and pan/zoom with the canvas.
- N-in/M-out is purely data-driven: `Node.Input`/`Node.Output` render one `NodeInput`/`NodeOutput` per element of `Inputs`/`Outputs`. For NodeModel-style special nodes (sliders, watch), add a per-VM-type `DataTemplate` in `NodifyEditor.Resources` (the Calculator does this for five node types) — the editor picks it by DataType, no `ItemTemplateSelector` needed; use `nodify:Node.ContentTemplate` to embed arbitrary WPF (Slider, TextBox) inside the node body.
- Heterogeneous canvas items (nodes + groups + notes) all live in the same `ItemsSource`; `GroupingNode` (with `CanResize`, `ActualSize` two-way, `MovementMode` `Group/Self`, `ResizeThumbTemplate`) covers Dynamo groups; `KnotNode` covers wire reroute points.
- For undo/redo and .dyc persistence hook `ItemsDragCompletedCommand`, `ItemsMoved`, and the connection commands; the VM collections are the single source of truth mirrored into the Core graph model.

**Everything above is confirmed present in Nodify 7.3.0's net48 assembly.** Facts deliberately *not* claimed: there is no built-in node-delete command, no built-in undo/redo, and no `ItemsSource` templating quirks beyond standard `MultiSelector` behavior. The Calculator example referenced styles `OriginalNodeInputStyle` — that key is app-local, not part of Nodify; use `BasedOn="{StaticResource {x:Type nodify:NodeInput}}"` instead as shown.
