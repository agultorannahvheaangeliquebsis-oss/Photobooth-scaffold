using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

/// <summary>
/// Resolves the connection string to the booth's LocalDB instance and hands
/// out open connections. A single booth machine talks to a single local
/// database, so this is intentionally not multi-tenant/configurable beyond
/// an environment variable override for dev machines that name their
/// LocalDB instance differently.
/// </summary>
public static class SqlConnectionFactory
{
    // Connect Timeout is explicit rather than relying on SqlClient's own
    // default: a missing or stopped LocalDB instance was observed hanging
    // well past that default rather than failing, leaving the booth on a
    // black screen instead of an error. 5s is enough for an automatic
    // instance to auto-start (confirmed: ~1s in practice) without making a
    // guest wait long on a genuinely dead instance.
    private const string DefaultConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=Photobooth;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5;";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("PHOTOBOOTH_DB_CONNECTION") ?? DefaultConnectionString;

    public static async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
