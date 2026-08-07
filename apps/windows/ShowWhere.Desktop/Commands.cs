using System.Windows.Input;

namespace ShowWhere.Desktop;

public sealed class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private bool _executing;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_executing && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _executing = true;
        RaiseCanExecuteChanged();
        try { await execute(); }
        finally { _executing = false; RaiseCanExecuteChanged(); }
    }
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class AsyncParameterRelayCommand(
    Func<object?, Task> execute,
    Func<object?, bool>? canExecute = null) : ICommand
{
    private bool _executing;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_executing && (canExecute?.Invoke(parameter) ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _executing = true;
        RaiseCanExecuteChanged();
        try { await execute(parameter); }
        finally { _executing = false; RaiseCanExecuteChanged(); }
    }
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
