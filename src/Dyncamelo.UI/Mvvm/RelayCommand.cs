using System;
using System.Windows.Input;

namespace Dyncamelo.UI.Mvvm;

/// <summary>
/// A parameterless <see cref="ICommand"/> delegating to callbacks.
/// Re-queries executability via <see cref="CommandManager.RequerySuggested"/>.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    /// <summary>Creates the command.</summary>
    /// <param name="execute">Invoked by <see cref="Execute"/>.</param>
    /// <param name="canExecute">Optional guard; the command is always executable when null.</param>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

    /// <inheritdoc />
    public void Execute(object? parameter) => _execute();

    /// <summary>Forces bound controls to re-query <see cref="CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}

/// <summary>
/// A typed <see cref="ICommand"/> delegating to callbacks.
/// Re-queries executability via <see cref="CommandManager.RequerySuggested"/>.
/// </summary>
/// <typeparam name="T">Command parameter type.</typeparam>
public class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    /// <summary>Creates the command.</summary>
    /// <param name="execute">Invoked by <see cref="Execute"/>.</param>
    /// <param name="canExecute">Optional guard; the command is always executable when null.</param>
    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <inheritdoc />
    public bool CanExecute(object? parameter)
    {
        return _canExecute == null || _canExecute(Cast(parameter));
    }

    /// <inheritdoc />
    public void Execute(object? parameter) => _execute(Cast(parameter));

    /// <summary>Forces bound controls to re-query <see cref="CanExecute"/>.</summary>
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();

    private static T? Cast(object? parameter)
    {
        return parameter is T typed ? typed : default;
    }
}
