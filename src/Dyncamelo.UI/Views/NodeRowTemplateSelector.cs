using System.Windows;
using System.Windows.Controls;
using Dyncamelo.Core.Editing;
using Dyncamelo.UI.ViewModels;

namespace Dyncamelo.UI.Views;

/// <summary>Picks the row template (Row.Output, Row.Input, Row.Body, Row.PanelHeader, Row.Hidden) from the row kind.</summary>
public sealed class NodeRowTemplateSelector : DataTemplateSelector
{
    /// <inheritdoc />
    public override DataTemplate? SelectTemplate(object? item, DependencyObject container)
    {
        if (!(item is NodeRowViewModel row) || !(container is FrameworkElement element))
        {
            return base.SelectTemplate(item, container);
        }

        string key;
        switch (row.Kind)
        {
            case RowKind.Output: key = "Row.Output"; break;
            case RowKind.Input: key = "Row.Input"; break;
            case RowKind.Body: key = "Row.Body"; break;
            case RowKind.PanelHeader: key = "Row.PanelHeader"; break;
            default: key = "Row.Hidden"; break;
        }

        return element.TryFindResource(key) as DataTemplate;
    }
}
