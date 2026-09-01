namespace Photobooth.Core;

/// <summary>Records what it "ran" for tests, same pattern as
/// MockEmailDeliveryService/MockSmsDeliveryService.</summary>
public class MockPostProcessingService : IPostProcessingService
{
    public List<(string ApplicationPath, string PhotoPath)> Runs { get; } = new();

    public void Run(string applicationPath, string photoPath)
    {
        Runs.Add((applicationPath, photoPath));
    }
}
