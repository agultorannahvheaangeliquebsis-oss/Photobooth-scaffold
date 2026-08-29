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
    internal class Program
    {
        private const string PipeName = "PhotoboothCameraBridge";
        private static readonly CameraDeviceManager Manager = new CameraDeviceManager();
        private static TaskCompletionSource<PhotoCapturedEventArgs> _pendingCapture;

        private static void Main(string[] args)
        {
            // Otherwise the manager happily "connects" to a laptop's built-in
            // webcam and CAPTURE silently targets that instead of the D3500 --
            // confirmed during the Day 1 spike (see README) when no DSLR was
            // attached and it picked up a UVC webcam as the selected device.
            Manager.DetectWebcams = false;

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
    }
}
