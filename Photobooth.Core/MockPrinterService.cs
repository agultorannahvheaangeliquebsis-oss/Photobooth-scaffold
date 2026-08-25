namespace Photobooth.Core;

/// <summary>
/// Fake printer for development. Delay roughly matches a dye-sub printer's
/// real cycle time so the Printing state in the UI feels accurate during
/// testing, rather than flashing by instantly.
/// </summary>
public class MockPrinterService : IPrinterService
{
    public async Task PrintAsync(string imagePath, CancellationToken ct = default)
    {
        await Task.Delay(2500, ct);
    }
}
