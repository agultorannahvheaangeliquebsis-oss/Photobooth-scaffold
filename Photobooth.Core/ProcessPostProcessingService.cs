using System.Diagnostics;

namespace Photobooth.Core;

/// <summary>Real IPostProcessingService: starts the configured application
/// as a plain child process, the photo's path quoted as its one argument.
/// Not awaited/tracked past launch -- see the interface doc for why this is
/// fire-and-forget by design.</summary>
public class ProcessPostProcessingService : IPostProcessingService
{
    public void Run(string applicationPath, string photoPath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(applicationPath, $"\"{photoPath}\"")
            {
                UseShellExecute = false,
            });
        }
        catch
        {
            // Best-effort -- see the interface doc: a bad path or missing
            // application shouldn't take down the guest session over a
            // side-channel post-processing hook.
        }
    }
}
