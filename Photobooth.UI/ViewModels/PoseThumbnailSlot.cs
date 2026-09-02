using System.Windows;
using System.Windows.Media;

namespace Photobooth.UI.ViewModels;

/// <summary>One slot in the Capture screen's pose thumbnail strip (see
/// KioskViewModel.PoseThumbnails) -- filled with that pose's own shot as
/// BoothStateMachine.PosePhotoCaptured lands, or shown blank/numbered until
/// then. Built fresh each multi-pose session (see KioskViewModel's
/// PoseChanged handler), one per PrintTemplate.RequiredPhotoCount slot.</summary>
public class PoseThumbnailSlot : ObservableObject
{
    public PoseThumbnailSlot(int number, bool showPlaceholderNumberWhenEmpty)
    {
        Number = number;
        _showPlaceholderNumberWhenEmpty = showPlaceholderNumberWhenEmpty;
    }

    /// <summary>1-based pose index -- the placeholder digit shown before this
    /// slot's shot lands.</summary>
    public int Number { get; }

    private readonly bool _showPlaceholderNumberWhenEmpty;

    private ImageSource? _imageSource;
    public ImageSource? ImageSource
    {
        get => _imageSource;
        set
        {
            if (SetProperty(ref _imageSource, value))
            {
                RaisePropertyChanged(nameof(HasImage));
                RaisePropertyChanged(nameof(ShowNumber));
            }
        }
    }

    public bool HasImage => ImageSource is not null;

    /// <summary>True only while this is the pose currently being counted down
    /// / captured -- drives the strip's "active shot" border highlight.</summary>
    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                RaisePropertyChanged(nameof(ActiveBorderThickness));
            }
        }
    }

    public Thickness ActiveBorderThickness => IsActive ? new Thickness(3) : new Thickness(0);

    /// <summary>ScreenSettings.PoseStripShowPlaceholderNumbers, snapshotted at
    /// session start -- shows this slot's number only while it has no image
    /// yet.</summary>
    public bool ShowNumber => !HasImage && _showPlaceholderNumberWhenEmpty;
}
