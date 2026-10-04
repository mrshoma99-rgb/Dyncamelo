using System.Windows;
using System.Windows.Controls;
using CamelGraph.Core.Graph;
using CamelGraph.Core.Nodes;
using CamelGraph.Core.Serialization;

namespace CamelGraph.UI.Views;

/// <summary>
/// Picks the inline-editor template for a node body from the wrapped
/// <see cref="NodeModel"/>'s concrete type. Types from node packs this assembly
/// does not reference (e.g. CamelGraph.Nodes) are matched by type name; unknown
/// types fall back to an empty body.
/// </summary>
public class NodeBodyTemplateSelector : DataTemplateSelector
{
    /// <inheritdoc />
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (!(container is FrameworkElement element))
        {
            return base.SelectTemplate(item, container);
        }

        var key = GetTemplateKey(item);
        return element.TryFindResource(key) as DataTemplate
            ?? element.TryFindResource("NodeBody.Empty") as DataTemplate;
    }

    private static string GetTemplateKey(object? model)
    {
        switch (model)
        {
            case NumberInputNode _:
                return "NodeBody.NumberInput";
            case NumberSliderNode _:
                return "NodeBody.NumberSlider";
            case IntegerSliderNode _:
                return "NodeBody.IntegerSlider";
            case StringInputNode _:
                return "NodeBody.StringInput";
            case IntegerInputNode _:
                return "NodeBody.IntegerInput";
            case DateInputNode _:
                return "NodeBody.DateInput";
            case ChoiceInputNode _:
                return "NodeBody.Choice";
            case BooleanToggleNode _:
                return "NodeBody.BooleanToggle";
            case FilePathNode _:
                return "NodeBody.FilePath";
            case DirectoryPathNode _:
                return "NodeBody.DirectoryPath";
            case WatchNode _:
                return "NodeBody.Watch";
            case MissingNodeModel _:
                return "NodeBody.Missing";
            case CamelGraph.Core.Groups.GroupInputNode _:
                return "NodeBody.GroupInterface";
            case CamelGraph.Core.Groups.GroupOutputNode _:
                return "NodeBody.GroupInterface";
            case null:
                return "NodeBody.Empty";
            default:
                // Reflection-friendly fallback for node packs (CamelGraph.Nodes):
                // templates bind by property name, which WPF resolves at runtime.
                return "NodeBody." + model.GetType().Name;
        }
    }
}
