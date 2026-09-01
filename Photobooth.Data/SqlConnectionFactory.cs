using Microsoft.Data.SqlClient;

namespace Photobooth.Data;

/// <summary>
/// Hands out open connections to the booth's database, using whatever
/// connection string <see cref="BoothConfiguration"/> resolves (env var
/// override, then the machine's AppData config file, then the LocalDB
/// default -- see that class for the full precedence). A single booth
/// machine talks to a single database, so this stays a thin wrapper rather
/// than anything multi-tenant.
/// </summary>
public static class SqlConnectionFactory
{
    // Connect Timeout is explicit rather than relying on SqlClient's own
    // default: a missing or stopped LocalDB instance was observed hanging
    // well past that default rather than failing, leaving the booth on a
    // black screen instead of an error. 5s is enough for an automatic
    // instance to auto-start (confirmed: ~1s in practice) without making a
    // guest wait long on a genuinely dead instance. Applied here (not baked
    // into BoothConfiguration's resolved string) so it still applies even
    // when an admin-supplied connection string omits it.
    private const string ConnectTimeoutClause = "Connect Timeout=5;";

    public static string ConnectionString => EnsureConnectTimeout(BoothConfiguration.ConnectionString);

    private static string EnsureConnectTimeout(string connectionString)
    {
        var builder = new SqlConnectionStringBuilder(connectionString);
        if (builder.ConnectTimeout == 15) // SqlClient's own default -- unset by the caller
        {
            connectionString = connectionString.TrimEnd();
            if (!connectionString.EndsWith(';'))
            {
                connectionString += ";";
            }
            connectionString += ConnectTimeoutClause;
        }
        return connectionString;
    }

    public static async Task<SqlConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
