namespace VFridge.Api.Configuration;

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    /// <summary>Explicit list of allowed origins. Ignored when <see cref="AllowAnyOrigin"/> is true.</summary>
    public string[] AllowedOrigins { get; set; } = [];

    /// <summary>
    /// When true, the API accepts requests from any origin (no credentials). This is safe for a
    /// bearer-token API — the Authorization header is opt-in per request and not auto-sent by the
    /// browser. The CORS spec forbids combining a wildcard origin with credentials, so when this
    /// is on we drop <c>AllowCredentials</c>.
    /// </summary>
    public bool AllowAnyOrigin { get; set; }
}
