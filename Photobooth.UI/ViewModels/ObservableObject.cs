using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Photobooth.UI.ViewModels;

/// <summary>
/// Minimal INotifyPropertyChanged base for this project's ViewModels. Hand
/// rolled rather than pulled from CommunityToolkit.Mvvm on purpose -- the
/// solution currently has four NuGet dependencies total (see the csproj
/// files), and a source generator is a heavy addition for the two members
/// actually needed here.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Assigns and raises PropertyChanged only when the value actually
    /// changed. Returns whether it changed, so callers can chain dependent
    /// notifications (e.g. a computed "CanPrint") off a real change.</summary>
    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }
}
