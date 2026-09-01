using DbUp.Engine.Output;
using Serilog;

namespace Photobooth.Data;

/// <summary>Routes DbUp's own migration-run logging (which script is being
/// applied, success/failure) through the app's shared Serilog logger instead
/// of DbUp's default console writer, so a migration run on a booth machine
/// with no attached console still ends up in the log file App.xaml.cs
/// configures at startup.</summary>
internal sealed class SerilogDbUpLogger : IUpgradeLog
{
    public void LogTrace(string format, params object[] args) => Log.Verbose(format, args);
    public void LogDebug(string format, params object[] args) => Log.Debug(format, args);
    public void LogInformation(string format, params object[] args) => Log.Information(format, args);
    public void LogWarning(string format, params object[] args) => Log.Warning(format, args);
    public void LogError(string format, params object[] args) => Log.Error(format, args);
    public void LogError(Exception ex, string format, params object[] args) => Log.Error(ex, format, args);
}
