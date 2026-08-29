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

        private static void Main(string[] args)
        {
            // Otherwise the manager happily "connects" to a laptop's built-in
            // webcam and CAPTURE silently targets that instead of the D3500 --
            // confirmed during the Day 1 spike (see README) when no DSLR was
            // attached and it picked up a UVC webcam as the selected device.
            // --allow-webcam opts back into that behavior on purpose, for
            // dev-machine testing when no D3500 is attached; never pass it
            // when running against the real booth hardware.
            bool allowWebcam = Array.Exists(args, a => a.Equals("--allow-webcam", StringComparison.OrdinalIgnoreCase));
            Manager.DetectWebcams = allowWebcam;
            Console.WriteLine(allowWebcam
                ? "[bridge] --allow-webcam set: will treat a laptop webcam as the camera. DEV/TEST ONLY."
                : "[bridge] webcam detection disabled: only a real PTP/tethered camera will be picked up.");

            Manager.CameraConnected += device =>
                Console.WriteLine($"[camera] connected: {device.DeviceName}");
            Manager.CameraDisconnected += device =>
                Console.WriteLine("[camera] disconnected");
            Manager.PhotoCaptured += Manager_PhotoCaptured;

            Console.WriteLine("[bridge] looking for a connected camera...");
            bool found = Manager.ConnectToCamera();
            Console.WriteLine(found
                ? $"[bridge] camera ready: {Manager.SelectedCameraDevice?.DeviceName}"
                : "[bridge] no camera detected -- pipe server will report ERR on CAPTURE until one connects");

            RunPipeServerLoop();
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
