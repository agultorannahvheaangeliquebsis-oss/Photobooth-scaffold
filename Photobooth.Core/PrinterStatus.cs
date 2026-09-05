using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// The printer is there but can't produce a print right now -- out of paper or
/// ribbon, offline, jammed, door open, or in an error state. Distinct from the
/// "not installed at all" InvalidOperationException SpoolerPrinterService
/// throws for an invalid PrinterSettings: this one means "fix the printer",
/// not "fix the configuration", and the session it happens in is still a
/// perfectly good session -- the guest's photo exists and has been uploaded.
/// </summary>
public sealed class PrinterUnavailableException(string message) : InvalidOperationException(message);

/// <summary>
/// Reads the Windows spooler's own status for a printer.
///
/// This exists because <c>PrintDocument.Print()</c> returns as soon as the job
/// is handed to the spooler, which is not the same thing as a print. Out of
/// paper, out of ribbon, offline, and jammed were all indistinguishable from
/// success: the guest was told "Printing -- your photo is on its way", a Print
/// row was written, and nobody found out until someone physically looked at
/// the printer. On an unattended booth that is the classic all-night failure --
/// one ribbon runs out at guest 40 and guests 41 through 200 are all told
/// their photo is coming.
///
/// P/Invoke into winspool rather than WMI or a vendor SDK: it's the same
/// information Windows' own printer queue window shows, needs no extra package
/// reference, and works for any driver-backed printer, which is the whole
/// reason SpoolerPrinterService goes through the spooler in the first place.
/// </summary>
[SupportedOSPlatform("windows")]
public static class PrinterStatus
{
    // Subset of the PRINTER_STATUS_* flags that mean "this job is not going to
    // come out on paper". Deliberately not every flag: PRINTER_STATUS_BUSY,
    // PRINTER_STATUS_PRINTING, PRINTER_STATUS_WARMING_UP and friends are all
    // normal states for a booth printer mid-shift and must never be reported
    // as a problem, or the attendant learns to ignore the alert.
    private const uint PaperJam = 0x00000008;
    private const uint PaperOut = 0x00000010;
    private const uint PaperProblem = 0x00000040;
    private const uint Offline = 0x00000080;
    private const uint OutputBinFull = 0x00000800;
    private const uint NotAvailable = 0x00001000;
    private const uint NoToner = 0x00040000;
    private const uint UserIntervention = 0x00100000;
    private const uint OutOfMemory = 0x00200000;
    private const uint DoorOpen = 0x00400000;
    private const uint ServerUnknown = 0x00800000;
    private const uint Error = 0x00000002;

    /// <summary>
    /// A human-readable reason the named printer can't print right now, or null
    /// if it looks fine. Null is also returned when the status genuinely can't
    /// be read (no such printer handle, access denied, a driver that reports
    /// nothing) -- an unreadable status must never block a print that would
    /// otherwise have worked, since the pre-existing behavior of just spooling
    /// and hoping is strictly better than refusing to print at all.
    /// </summary>
    public static string? DescribeProblem(string? printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return null;
        }

        uint status;
        try
        {
            if (!TryReadStatus(printerName, out status))
            {
                return null;
            }
        }
        catch (Exception)
        {
            // Any interop failure means "couldn't tell" -- see the doc above.
            return null;
        }

        if ((status & PaperOut) != 0) return "The printer is out of paper.";
        if ((status & NoToner) != 0) return "The printer is out of ribbon or ink.";
        if ((status & PaperJam) != 0) return "The printer has a paper jam.";
        if ((status & DoorOpen) != 0) return "The printer's door or cover is open.";
        if ((status & OutputBinFull) != 0) return "The printer's output tray is full.";
        if ((status & PaperProblem) != 0) return "The printer has a paper problem.";
        if ((status & (Offline | NotAvailable | ServerUnknown)) != 0) return "The printer is offline.";
        if ((status & OutOfMemory) != 0) return "The printer is out of memory.";
        if ((status & UserIntervention) != 0) return "The printer needs attention.";
        if ((status & Error) != 0) return "The printer is reporting an error.";

        return null;
    }

    private static bool TryReadStatus(string printerName, out uint status)
    {
        status = 0;

        if (!OpenPrinter(printerName, out IntPtr printer, IntPtr.Zero))
        {
            return false;
        }

        try
        {
            // Level 2 (PRINTER_INFO_2) is the level that carries Status. The
            // documented two-call pattern: ask with a zero-length buffer to
            // learn the size, then ask again with a buffer that big.
            GetPrinter(printer, 2, IntPtr.Zero, 0, out int needed);
            if (needed <= 0)
            {
                return false;
            }

            IntPtr buffer = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetPrinter(printer, 2, buffer, needed, out _))
                {
                    return false;
                }

                var info = Marshal.PtrToStructure<PrinterInfo2>(buffer);
                status = info.Status;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ClosePrinter(printer);
        }
    }

    /// <summary>PRINTER_INFO_2. Only <see cref="Status"/> is read, but the whole
    /// layout has to be declared for the preceding fields' offsets to land
    /// correctly. IntPtr for every pointer field so this marshals identically
    /// under x86 and x64.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PrinterInfo2
    {
        public IntPtr ServerName;
        public IntPtr PrinterName;
        public IntPtr ShareName;
        public IntPtr PortName;
        public IntPtr DriverName;
        public IntPtr Comment;
        public IntPtr Location;
        public IntPtr DevMode;
        public IntPtr SepFile;
        public IntPtr PrintProcessor;
        public IntPtr Datatype;
        public IntPtr Parameters;
        public IntPtr SecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint Jobs;
        public uint AveragePpm;
    }

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "OpenPrinterW")]
    private static extern bool OpenPrinter(string printerName, out IntPtr printer, IntPtr defaults);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr printer);

    [DllImport("winspool.drv", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetPrinterW")]
    private static extern bool GetPrinter(IntPtr printer, int level, IntPtr printerInfo, int size, out int needed);
}
