using System.Windows;
using System.Windows.Controls;
using Dyncamelo.Core.Graph;
using Dyncamelo.Core.Nodes;
using Dyncamelo.Core.Serialization;

namespace Dyncamelo.UI.Views;

/// <summary>
/// Picks the inline-editor template for a node body from the wrapped
/// <see cref="NodeModel"/>'s concrete type. Types from node packs this assembly
/// does not reference (e.g. Dyncamelo.Nodes) are matched by type name; unknown
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
            case null:
                return "NodeBody.Empty";
            default:
                // Reflection-friendly fallback for node packs (Dyncamelo.Nodes):
                // templates bind by property name, which WPF resolves at runtime.
                return "NodeBody." + model.GetType().Name;
        }
    }
}
