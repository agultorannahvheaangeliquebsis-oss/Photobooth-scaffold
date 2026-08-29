using System.IO.Pipes;
using System.Text;

// Throwaway net8.0 test client for the Day 1 spike: proves a net8.0 process
// can round-trip commands over the named pipe to the net48/x86
// Photobooth.CameraBridge.Host process. This is what ICameraService's real
// implementation will do in Day 2 -- this project just proves the pipe
// boundary itself works before building the real service around it.
const string PipeName = "PhotoboothCameraBridge";
string[] commands = args.Length > 0 ? args : new[] { "PING", "STATUS", "CAPTURE" };

foreach (var command in commands)
{
    using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
    Console.Write($"> {command} ... ");

    try
    {
        pipe.Connect(3000);
    }
    catch (TimeoutException)
    {
        Console.WriteLine("FAILED: could not connect to bridge host (is Photobooth.CameraBridge.Host running?)");
        continue;
    }

    var writer = new StreamWriter(pipe, Encoding.ASCII) { AutoFlush = true };
    var reader = new StreamReader(pipe, Encoding.ASCII);

    writer.WriteLine(command);
    string? response = reader.ReadLine();
    Console.WriteLine(response);
}
