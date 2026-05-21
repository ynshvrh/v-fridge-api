namespace VFridge.Api.Infrastructure;

/// <summary>
/// Bridges between libpq-style connection URIs ("postgresql://user:pass@host/db?sslmode=require")
/// and the ADO.NET key=value form Npgsql consumes. Npgsql 10 accepts URIs natively, but real-world
/// providers (Neon, Supabase) append query params Npgsql does not understand — channel_binding is
/// the common one — which causes the parser to fail. We strip those and emit a clean key=value form.
/// </summary>
public static class NpgsqlConnectionString
{
    public static string Normalize(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;

        if (!raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        var uri = new Uri(raw);
        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var database = uri.AbsolutePath.TrimStart('/');

        var sslMode = "Require";
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                sslMode = kv[1] switch
                {
                    "require" => "Require",
                    "verify-ca" => "VerifyCA",
                    "verify-full" => "VerifyFull",
                    "disable" => "Disable",
                    _ => "Require"
                };
            }
        }

        var port = uri.Port > 0 ? uri.Port : 5432;
        return $"Host={uri.Host};Port={port};Database={database};Username={user};Password={password};SslMode={sslMode};Pooling=true;";
    }
}
