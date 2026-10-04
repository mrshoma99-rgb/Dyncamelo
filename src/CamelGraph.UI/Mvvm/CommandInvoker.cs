using System.Windows;
using System.Windows.Input;

namespace CamelGraph.UI.Mvvm;

/// <summary>Runs an <see cref="ICommand"/> that may be a routed command needing a target element.</summary>
public static class CommandInvoker
{
    /// <summary>True when the command can run right now.</summary>
    /// <param name="command">The command.</param>
    /// <param name="routedTarget">Element a routed command is executed against (the canvas).</param>
    public static bool CanRun(ICommand command, IInputElement? routedTarget)
    {
        return command is RoutedCommand routed ? routed.CanExecute(null, routedTarget) : command.CanExecute(null);
    }

    /// <summary>Runs the command when it can run; returns whether it did.</summary>
    /// <param name="command">The command.</param>
    /// <param name="routedTarget">Element a routed command is executed against (the canvas).</param>
    public static bool Run(ICommand command, IInputElement? routedTarget)
    {
        if (!CanRun(command, routedTarget))
        {
            return false;
        }

        if (command is RoutedCommand routed)
        {
            routed.Execute(null, routedTarget);
        }
        else
        {
            command.Execute(null);
        }

        return true;
    }
}
