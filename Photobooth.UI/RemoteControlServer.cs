using System.Net;
using System.Text;
using Serilog;

namespace Photobooth.UI;

/// <summary>
/// The scoped-down "remote/attendant control" feature BUILD_PLAN.md's Day 6
/// entry originally called for and never built -- not a full companion
/// mobile app, just what an attendant standing near (not at) the booth
/// actually needs: see the current booth state and start the next guest
/// without walking up to the kiosk. A loopback-only <see cref="HttpListener"/>,
/// same "local process exposing a small protocol" shape
/// Photobooth.CameraBridge.Host/.Client already proved out for the camera,
/// just HTTP-over-loopback instead of a named pipe -- an attendant's own
/// phone/laptop browser on the same machine or LAN can hit it directly,
/// which a named pipe can't offer.
///
/// No Core interface/mock: unlike BoothServices' seams, nothing in
/// BoothStateMachine depends on this, and there's no business logic here
/// worth faking in a unit test -- just two callbacks into whatever already
/// owns the real state (see KioskViewModel.ApplyRemoteControlEnabled).
///
/// Threading: HttpListener's GetContextAsync loop runs on its own background
/// thread. Both callback parameters are invoked from that thread and must
/// marshal onto the UI thread themselves (KioskViewModel's callbacks do this
/// via Dispatcher.Invoke) -- this class does no marshaling of its own.
/// </summary>
public sealed class RemoteControlServer : IDisposable
{
    /// <summary>Loopback only -- "localhost", never "+" or a real hostname/IP,
    /// so this never needs (or gets) a URL ACL reservation or admin rights,
    /// and is unreachable from outside this machine. Same "this machine only"
    /// trust boundary the camera bridge already assumes for its own pipe.</summary>
    private const string Prefix = "http://localhost:5197/";

    private readonly Func<string> _getStatus;
    private readonly Func<bool> _tryStartNextGuest;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;
    private Task? _listenLoop;
    private bool _disposed;

    public RemoteControlServer(Func<string> getStatus, Func<bool> tryStartNextGuest)
    {
        _getStatus = getStatus;
        _tryStartNextGuest = tryStartNextGuest;
        _listener.Prefixes.Add(Prefix);
    }

    /// <summary>The address an attendant's browser hits (see AdminWindow's
    /// Remote Control section, which shows this verbatim).</summary>
    public static string Url => Prefix;

    public void Start()
    {
        _listener.Start();
        _cts = new CancellationTokenSource();
        _listenLoop = Task.Run(() => ListenLoopAsync(_cts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (ct.IsCancellationRequested || _disposed)
            {
                // Listener was stopped/disposed out from under a pending
                // GetContextAsync call -- expected on shutdown, not a real error.
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Remote Control listener error");
                continue;
            }

            _ = HandleRequestAsync(context);
        }
    }

    private Task HandleRequestAsync(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath ?? "/";
            string method = context.Request.HttpMethod;

            if (method == "GET" && path is "/" or "/status")
            {
                string state = _getStatus();
                WriteJson(context.Response, 200, $$"""{"state":"{{state}}"}""");
            }
            else if (method == "POST" && path == "/start-next")
            {
                bool started = _tryStartNextGuest();
                WriteJson(context.Response, started ? 200 : 409, $$"""{"ok":{{(started ? "true" : "false")}}}""");
            }
            else
            {
                WriteJson(context.Response, 404, """{"error":"not found"}""");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Remote Control request handling error");
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch (Exception)
            {
                // Best-effort -- the response may already be unusable if the
                // exception above happened mid-write.
            }
        }

        return Task.CompletedTask;
    }

    private static void WriteJson(HttpListenerResponse response, int statusCode, string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.OutputStream.Close();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        _cts?.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (Exception)
        {
            // Best-effort -- nothing left to clean up if this throws.
        }
        _cts?.Dispose();
    }
}
