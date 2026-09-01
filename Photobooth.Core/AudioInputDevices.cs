using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Lists the system's audio-input (microphone) device names for AdminWindow's
/// Camera Settings "Audio Input" dropdown (see dslrBooth's own Camera Settings
/// screen). P/Invokes the legacy winmm waveIn API rather than pulling in a
/// NAudio/MMDevice NuGet package -- this only needs device *names* for a
/// picker, not audio capture itself (FfmpegVideoGuestbookService's ffmpeg
/// dshow input does the actual capturing, keyed by device name string already),
/// and winmm ships with every Windows install with no extra dependency.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AudioInputDevices
{
    // MAXPNAMELEN (mmsystem.h) -- the legacy waveIn API truncates device names
    // to 31 characters plus a null terminator, same truncation dslrBooth's own
    // Camera Settings screen shows for a long device name.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WAVEINCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint dwFormats;
        public ushort wChannels;
        public ushort wReserved1;
    }

    [DllImport("winmm.dll")]
    private static extern uint waveInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern uint waveInGetDevCaps(nuint uDeviceID, out WAVEINCAPS pwic, uint cbwic);

    /// <summary>Returns every audio-input device's display name, in device-index
    /// order. Empty if the API reports no devices (e.g. this machine has no
    /// microphone) rather than throwing -- callers should treat that the same
    /// as "use the system default" (see ScreenSettings.AudioInputDeviceName).</summary>
    public static List<string> EnumerateNames()
    {
        var names = new List<string>();
        uint count = waveInGetNumDevs();
        for (uint deviceId = 0; deviceId < count; deviceId++)
        {
            if (waveInGetDevCaps(deviceId, out WAVEINCAPS caps, (uint)Marshal.SizeOf<WAVEINCAPS>()) == 0)
            {
                names.Add(caps.szPname);
            }
        }
        return names;
    }
}
