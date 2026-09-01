using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Photobooth.Data;

/// <summary>
/// Resolves booth-machine settings (currently just the DB connection string)
/// from an admin-editable file in the current user's AppData instead of a
/// value baked into the shipped binary -- so the same install can point at a
/// differently-named LocalDB instance, a full SQL Server, or a
/// password-bearing connection string per machine without a rebuild.
/// Precedence: PHOTOBOOTH_DB_CONNECTION env var (dev override, unchanged
/// from before this file existed) > ConnectionStringEncrypted (DPAPI,
/// current-user scope -- for connection strings carrying a SQL login
/// password) > ConnectionString (plain -- fine for the common
/// Trusted_Connection case, which carries no secret) > the LocalDB default.
/// </summary>
public static class BoothConfiguration
{
    private sealed class ConfigFile
    {
        public string? ConnectionString { get; set; }
        public string? ConnectionStringEncrypted { get; set; }
    }

    // Same value SqlConnectionFactory hardcoded before this file existed --
    // kept as the last-resort fallback so a machine with no config file yet
    // (a fresh dev checkout) behaves exactly as it did previously.
    private const string DefaultConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=Photobooth;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5;";

    private static readonly string ConfigDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Photobooth");

    private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "appsettings.json");

    private static readonly Lazy<string> Resolved = new(Resolve);

    public static string ConnectionString => Resolved.Value;

    private static string Resolve()
    {
        string? envOverride = Environment.GetEnvironmentVariable("PHOTOBOOTH_DB_CONNECTION");
        if (!string.IsNullOrEmpty(envOverride))
        {
            return envOverride;
        }

        ConfigFile? config = TryReadConfigFile();
        if (config?.ConnectionStringEncrypted is { Length: > 0 } encrypted)
        {
            return Unprotect(encrypted);
        }
        if (config?.ConnectionString is { Length: > 0 } plain)
        {
            return plain;
        }

        return DefaultConnectionString;
    }

    private static ConfigFile? TryReadConfigFile()
    {
        if (!File.Exists(ConfigFilePath))
        {
            return null;
        }

        try
        {
            string json = File.ReadAllText(ConfigFilePath);
            return JsonSerializer.Deserialize<ConfigFile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Malformed/unreadable config shouldn't strand the booth on a
            // black screen -- fall back to the default connection string
            // same as a missing file, and let DatabaseInitializer's own
            // connect-and-fail path surface the real problem if the default
            // doesn't work either.
            return null;
        }
    }

    /// <summary>DPAPI-encrypts <paramref name="plainConnectionString"/> for the
    /// current Windows user, returning the base64 payload to place in
    /// appsettings.json's ConnectionStringEncrypted field. Current-user scope
    /// (not machine scope) so the value only decrypts under the same Windows
    /// account the booth app runs as -- intended to be run once by an admin
    /// setting up a machine (e.g. from a small setup script or the Package
    /// Manager Console), not at booth runtime.</summary>
    // DPAPI (ProtectedData) is Windows-only, same as System.Drawing.Common
    // elsewhere in this project -- fine, since this whole solution already
    // only runs on the Windows booth machine. Suppressed rather than
    // [SupportedOSPlatform]-annotated so the warning doesn't cascade to
    // SqlConnectionFactory.ConnectionString's ~20 call sites across the
    // solution, none of which touch DPAPI themselves.
#pragma warning disable CA1416
    public static string Protect(string plainConnectionString)
    {
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainConnectionString);
        byte[] protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string encryptedBase64)
    {
        byte[] protectedBytes = Convert.FromBase64String(encryptedBase64);
        byte[] plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plainBytes);
    }
#pragma warning restore CA1416

    /// <summary>Writes (or overwrites) appsettings.json with a plain
    /// connection string. Convenience for admin setup tooling; booth runtime
    /// code never calls this.</summary>
    public static void WriteConnectionString(string connectionString, bool encrypt = false)
    {
        Directory.CreateDirectory(ConfigDirectory);
        var config = encrypt
            ? new ConfigFile { ConnectionStringEncrypted = Protect(connectionString) }
            : new ConfigFile { ConnectionString = connectionString };
        File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }
}
