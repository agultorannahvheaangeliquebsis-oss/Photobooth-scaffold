using System.Windows.Input;

namespace Photobooth.UI.ViewModels;

/// <summary>Synchronous ICommand backed by a delegate pair.</summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    /// <summary>Re-queries CanExecute. Raised explicitly by the ViewModel rather
    /// than routed through CommandManager.RequerySuggested: on a kiosk the only
    /// things that change a command's availability are state-machine transitions
    /// and settings reloads, both of which the ViewModel already observes, and
    /// RequerySuggested would re-evaluate on every keystroke/mouse move for no
    /// benefit.</summary>
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// ICommand for async handlers. Reports CanExecute=false while the previous
/// invocation is still running, so a guest double-tapping Print on a
/// touchscreen can't queue a second spool job -- the single most likely
/// double-fire on this UI.
/// </summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Func<object?, bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<object?, Task> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged;

    /// <summary>Where a faulted handler goes. ICommand.Execute is void, so an
    /// unhandled exception in an async handler would otherwise tear down the
    /// whole app from a background continuation -- on an unattended booth that
    /// means a black screen mid-event. The ViewModel points this at its own
    /// status/error surface.</summary>
    public Action<Exception>? ExceptionHandler { get; set; }

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter);
        }
        catch (Exception ex)
        {
            ExceptionHandler?.Invoke(ex);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
