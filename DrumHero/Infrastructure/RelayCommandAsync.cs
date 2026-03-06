using System.Windows.Input;

namespace DrumHero.Infrastructure;

/// <summary>
/// Async relay command that properly handles async operations.
/// </summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isExecuting;
    
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
    
    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    
    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke() ?? true);
    
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        
        _isExecuting = true;
        CommandManager.InvalidateRequerySuggested();
        
        try
        {
            await _execute();
        }
        finally
        {
            _isExecuting = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }
}

public class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T, Task> _execute;
    private readonly Func<T, bool>? _canExecute;
    private bool _isExecuting;
    
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
    
    public AsyncRelayCommand(Func<T, Task> execute, Func<T, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    
    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke((T)parameter!) ?? true);
    
    public async void Execute(object? parameter)
    {
        if (parameter is T typed && CanExecute(parameter))
        {
            _isExecuting = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                await _execute(typed);
            }
            finally
            {
                _isExecuting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
