using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CameraControl.Devices;
using CameraControl.Devices.Classes;

namespace Photobooth.CameraBridge.Host
{
    // Out-of-process bridge to the Nikon D3500 via CameraControl.Devices
    // (the digiCamControl library). This has to run as a separate net48/x86
    // process -- CameraControl.Devices targets net46 and its bundled PTP/EDSDK
    // interop only loads under x86 (confirmed via BadImageFormatException
    // until PlatformTarget was pinned to x86) -- so it can't be referenced
    // in-process from the net8.0 app. Talks to the rest of the app over a
    // named pipe instead.
    //
    // Protocol (newline-terminated ASCII commands over the pipe):
    //   PING            -> PONG
    //   STATUS          -> CONNECTED <camera name>  |  DISCONNECTED
    //   CAPTURE         -> OK <absolute file path>  |  ERR <message>
    //   LIVEVIEW        -> OK <base64 jpeg frame>    |  ERR <message>
    //   LIVEVIEW_STOP   -> OK
    internal class Program
    {
        private const string PipeName = "PhotoboothCameraBridge";
        private static readonly CameraDeviceManager Manager = new CameraDeviceManager();
        private static TaskCompletionSource<PhotoCapturedEventArgs> _pendingCapture;
        private static bool _liveViewStarted;
        private static bool _requireDslr;
        private static readonly object RescanLock = new object();

        private static void Main(string[] args)
        {
            // --require-dslr opts out of the fallback below, for production
            // booth hardware where a laptop webcam should never silently
            // stand in for the D3500. Leave it unset everywhere else so the
            // bridge picks up whatever camera the device actually has.
            bool requireDslr = Array.Exists(args, a => a.Equals("--require-dslr", StringComparison.OrdinalIgnoreCase));
            _requireDslr = requireDslr;

            Manager.CameraConnected += device =>
                Console.WriteLine($"[camera] connected: {device.DeviceName}");
            Manager.CameraDisconnected += device =>
                Console.WriteLine("[camera] disconnected");
            Manager.PhotoCaptured += Manager_PhotoCaptured;

            // Detect in two passes rather than just setting DetectWebcams up
            // front: a real PTP/tethered camera (the D3500) always takes
            // priority when one is attached, since the pass below never even
            // considers webcams. Only when nothing turns up on that pass do
            // we widen the search to "whatever camera this device has" --
            // confirmed during the Day 1 spike (see README) that with
            // DetectWebcams on the manager will happily "connect" to a
            // laptop's built-in webcam, so it should never be enabled while a
            // real camera might still be found without it.
            Console.WriteLine("[bridge] looking for a connected camera (DSLR/tethered)...");
            Manager.DetectWebcams = false;
            Manager.ConnectToCamera();
            bool found = WaitForSelectedCamera(TimeSpan.FromSeconds(2));

            if (!found && !requireDslr)
            {
                Console.WriteLine("[bridge] no DSLR found -- widening search to include this device's webcam...");
                Manager.DetectWebcams = true;
                Manager.ConnectToCamera();
                found = WaitForSelectedCamera(TimeSpan.FromSeconds(2));
            }

            Console.WriteLine(found
                ? $"[bridge] camera ready: {Manager.SelectedCameraDevice?.DeviceName}"
                : requireDslr
                    ? "[bridge] no DSLR detected and --require-dslr was set -- pipe server will report ERR on CAPTURE until one connects"
                    : "[bridge] no camera detected at all (checked DSLR and webcam) -- pipe server will report ERR on CAPTURE until one connects");

            // The two-pass scan above only runs once, at startup. If the
            // camera was in use by another app (e.g. its manufacturer
            // software) at that moment, PTP claim fails silently and no
            // amount of closing that other app afterward would ever get
            // picked up -- nothing re-triggers detection, since the USB
            // device itself never disconnects/reconnects. Keep retrying in
            // the background for as long as no camera is connected so a
            // guest doesn't need to restart the whole booth app just because
            // the DSLR was briefly held by something else.
            var rescanTimer = new Timer(_ => RescanIfDisconnected(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
            GC.KeepAlive(rescanTimer);

            RunPipeServerLoop();
        }

        // Runs on a background timer thread while the pipe server loop blocks
        // the main thread on WaitForConnection -- guarded by RescanLock so a
        // slow scan can't overlap itself if a previous tick is still running.
        private static void RescanIfDisconnected()
        {
            if (Manager.SelectedCameraDevice is { IsConnected: true })
            {
                return;
            }

            if (!Monitor.TryEnter(RescanLock))
            {
                return;
            }

            try
            {
                if (Manager.SelectedCameraDevice is { IsConnected: true })
                {
                    return;
                }

                Manager.DetectWebcams = false;
                Manager.ConnectToCamera();
                bool found = WaitForSelectedCamera(TimeSpan.FromSeconds(2));

                if (!found && !_requireDslr)
                {
                    Manager.DetectWebcams = true;
                    Manager.ConnectToCamera();
                    found = WaitForSelectedCamera(TimeSpan.FromSeconds(2));
                }

                if (found)
                {
                    Console.WriteLine($"[bridge] camera (re)detected: {Manager.SelectedCameraDevice?.DeviceName}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[bridge] rescan failed: {ex.Message}");
            }
            finally
            {
                Monitor.Exit(RescanLock);
            }
        }

        // ConnectToCamera()'s own return value isn't reliable -- confirmed on
        // a dev machine (webcam fallback path) that it can come back false on
        // the same call whose CameraConnected event and SelectedCameraDevice
        // show a successful connection moments later, presumably because the
        // manager finishes selecting the device asynchronously after
        // ConnectToCamera() returns. Poll SelectedCameraDevice instead --
        // and require IsConnected too, not just non-null: also confirmed a
        // device can be assigned to SelectedCameraDevice as a not-yet-ready
        // placeholder before the handshake completes (STATUS/CAPTURE below
        // both gate on IsConnected for the same reason).
        private static bool WaitForSelectedCamera(TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (Manager.SelectedCameraDevice is not { IsConnected: true } && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }
            return Manager.SelectedCameraDevice is { IsConnected: true };
        }

        private static void Manager_PhotoCaptured(object sender, PhotoCapturedEventArgs eventArgs)
        {
            _pendingCapture?.TrySetResult(eventArgs);
        }

        private static void RunPipeServerLoop()
        {
            Console.WriteLine($"[bridge] listening on pipe '{PipeName}'. Ctrl+C to stop.");
            while (true)
            {
                using (var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1))
                {
                    pipe.WaitForConnection();
                    try
                    {
                        HandleClient(pipe);
                    }
                    catch (IOException)
                    {
                        // client disconnected mid-command; move on to the next connection
                    }
                }
            }
        }

        private static void HandleClient(NamedPipeServerStream pipe)
        {
            var reader = new StreamReader(pipe, Encoding.ASCII, false, 1024, leaveOpen: true);
            var writer = new StreamWriter(pipe, Encoding.ASCII, 1024, leaveOpen: true) { AutoFlush = true };

            string command = reader.ReadLine();
            switch (command)
            {
                case "PING":
                    writer.WriteLine("PONG");
                    break;

                case "STATUS":
                    var device = Manager.SelectedCameraDevice;
                    writer.WriteLine(device != null && device.IsConnected
                        ? $"CONNECTED {device.DeviceName}"
                        : "DISCONNECTED");
                    break;

                case "CAPTURE":
                    writer.WriteLine(HandleCapture());
                    break;

                case "LIVEVIEW":
                    writer.WriteLine(HandleLiveViewFrame());
                    break;

                case "LIVEVIEW_STOP":
                    writer.WriteLine(HandleLiveViewStop());
                    break;

                default:
                    writer.WriteLine($"ERR unknown command '{command}'");
                    break;
            }
        }

        private static string HandleCapture()
        {
            var device = Manager.SelectedCameraDevice;
            if (device == null || !device.IsConnected)
            {
                return "ERR no camera connected";
            }

            // Some PTP cameras refuse a full-resolution capture while live
            // view is still streaming -- the WPF client already stops live
            // view before triggering Capturing, but do it here too so a
            // capture is never blocked by it regardless of client timing.
            if (_liveViewStarted)
            {
                try { device.StopLiveView(); } catch (Exception) { /* best-effort */ }
                _liveViewStarted = false;
            }

            _pendingCapture = new TaskCompletionSource<PhotoCapturedEventArgs>();
            try
            {
                device.CapturePhoto();
            }
            catch (Exception ex)
            {
                return $"ERR capture failed to start: {ex.Message}";
            }

            var completed = _pendingCapture.Task.Wait(TimeSpan.FromSeconds(10));
            if (!completed)
            {
                return "ERR capture timed out waiting for PhotoCaptured event";
            }

            var eventArgs = _pendingCapture.Task.Result;
            Directory.CreateDirectory("captures");
            string path = Path.GetFullPath(Path.Combine("captures", $"nikon_{DateTime.Now:yyyyMMdd_HHmmss}.jpg"));

            // A real PTP camera hands back a device handle that Transfer()
            // pulls the file through over PTP. The UVC webcam used as a
            // stand-in when no D3500 is attached hands back the raw JPEG
            // bytes as Handle instead -- Transfer() has nothing to pull from
            // in that case, so write the bytes directly.
            if (eventArgs.Handle is byte[] rawBytes)
            {
                try
                {
                    File.WriteAllBytes(path, rawBytes);
                }
                catch (Exception ex)
                {
                    return $"ERR writing raw capture bytes failed: {ex.Message}";
                }

                return File.Exists(path) ? $"OK {path}" : "ERR raw capture bytes written but file missing";
            }

            bool transferred;
            try
            {
                transferred = eventArgs.Transfer(path);
            }
            catch (Exception ex)
            {
                return $"ERR transfer threw: {ex.Message}";
            }

            if (!transferred || !File.Exists(path))
            {
                return $"ERR transfer reported failure (FileName='{eventArgs.FileName}', Handle='{eventArgs.Handle}')";
            }

            return $"OK {path}";
        }

        /// <summary>Triggers one CapturePhoto() cycle and returns the raw image
        /// bytes without saving a permanent file -- used by the live-view
        /// fallback for devices that don't implement the LiveView API.</summary>
        private static byte[] CaptureFrameBytes(ICameraDevice device)
        {
            _pendingCapture = new TaskCompletionSource<PhotoCapturedEventArgs>();
            device.CapturePhoto();

            var completed = _pendingCapture.Task.Wait(TimeSpan.FromSeconds(5));
            if (!completed)
            {
                throw new TimeoutException("capture timed out waiting for PhotoCaptured event");
            }

            var eventArgs = _pendingCapture.Task.Result;
            if (eventArgs.Handle is byte[] rawBytes)
            {
                return rawBytes;
            }

            string tempPath = Path.GetTempFileName();
            try
            {
                if (!eventArgs.Transfer(tempPath) || !File.Exists(tempPath))
                {
                    throw new InvalidOperationException($"transfer reported failure (FileName='{eventArgs.FileName}')");
                }
                return File.ReadAllBytes(tempPath);
            }
            finally
            {
                try { File.Delete(tempPath); } catch (Exception) { /* best-effort cleanup */ }
            }
        }

        private static string HandleLiveViewFrame()
        {
            var device = Manager.SelectedCameraDevice;
            if (device == null || !device.IsConnected)
            {
                return "ERR no camera connected";
            }

            if (!device.HaveLiveView)
            {
                // Confirmed for the UVC webcam stand-in used when no D3500 is
                // attached: this library's webcam wrapper never sets
                // HaveLiveView, so StartLiveView/GetLiveViewImage aren't an
                // option. Fall back to repeated snapshot captures instead --
                // measured ~130ms per round trip for the webcam, fast enough
                // to feel live at this bridge's polling rate. A real D3500
                // is expected to report HaveLiveView=true and use the proper
                // path above; unverified until one's connected.
                try
                {
                    return "OK " + Convert.ToBase64String(CaptureFrameBytes(device));
                }
                catch (Exception ex)
                {
                    return $"ERR live view fallback capture failed: {ex.Message}";
                }
            }

            try
            {
                if (!_liveViewStarted)
                {
                    device.StartLiveView();
                    _liveViewStarted = true;
                    Thread.Sleep(200); // give the device a moment to warm up before the first frame
                }

                LiveViewData data = null;
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    data = device.GetLiveViewImage();
                    if (data?.ImageData != null && data.ImageData.Length > data.ImageDataPosition)
                    {
                        break;
                    }
                    Thread.Sleep(50);
                }

                if (data?.ImageData == null || data.ImageData.Length <= data.ImageDataPosition)
                {
                    return "ERR no live view frame available";
                }

                int offset = data.ImageDataPosition;
                int length = data.ImageData.Length - offset;
                return "OK " + Convert.ToBase64String(data.ImageData, offset, length);
            }
            catch (Exception ex)
            {
                return $"ERR live view failed: {ex.Message}";
            }
        }

        private static string HandleLiveViewStop()
        {
            try
            {
                if (_liveViewStarted)
                {
                    Manager.SelectedCameraDevice?.StopLiveView();
                    _liveViewStarted = false;
                }
                return "OK";
            }
            catch (Exception ex)
            {
                return $"ERR {ex.Message}";
            }
        }
    }
}
