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
    private const string DefaultConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=Photobooth;Trusted_Connection=True;TrustServerCertificate=True;";

    public static string ConnectionString =>
        Environment.GetEnvironmentVariable("PHOTOBOOTH_DB_CONNECTION") ?? DefaultConnectionString;

    public static async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
