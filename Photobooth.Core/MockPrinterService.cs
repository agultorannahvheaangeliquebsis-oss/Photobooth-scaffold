namespace Photobooth.Core;

/// <summary>
/// Fake printer for development. Delay roughly matches a dye-sub printer's
/// real cycle time so the Printing state in the UI feels accurate during
/// testing, rather than flashing by instantly.
/// </summary>
public class MockPrinterService : IPrinterService
{
    /// <summary>Every template this mock was asked to print with, in call order --
    /// lets tests/demo confirm BoothStateMachine actually passed the admin's
    /// current PrintTemplate through, not just that PrintAsync got called.</summary>
    public List<PrintTemplate> PrintedTemplates { get; } = new();

    public async Task PrintAsync(string imagePath, PrintTemplate template, CancellationToken ct = default)
    {
        PrintedTemplates.Add(template);
        await Task.Delay(2500, ct);
    }
}
