namespace Photobooth.Core;

public sealed class BoothSettingsChangedEventArgs(int locationId) : EventArgs
{
    public int LocationId { get; } = locationId;
}

public static class BoothSettingsChanged
{
    public static event EventHandler<BoothSettingsChangedEventArgs>? Changed;

    public static void Publish(int locationId) =>
        Changed?.Invoke(null, new BoothSettingsChangedEventArgs(locationId));
}