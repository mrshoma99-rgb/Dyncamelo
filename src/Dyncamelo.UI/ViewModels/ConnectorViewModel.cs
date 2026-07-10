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
        set => SetProperty(ref _isConnected, value);
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
