namespace IncidentFlow.Api.Security;

// All the settings our backend needs, read once from configuration
// (environment variables / appsettings.json - see Program.cs) into one typed
// place instead of scattering Configuration["..."] lookups everywhere.
public class AppOptions
{
    public string PostgresConnectionString { get; init; } = string.Empty;

    public string RedisConnectionString { get; init; } = string.Empty;

    // Secret key used to sign/verify JWT login tokens. Never commit a real
    // secret to git - the default in appsettings.json is only for local dev.
    // HMAC-SHA256 requires a key of at least 256 bits (32 bytes), so this
    // must stay at least that long or token creation throws at runtime.
    public string JwtSecret { get; init; } = "dev-secret-change-me-to-a-long-random-string";

    // How long a login token stays valid, in minutes (default: 24h).
    public int AccessTokenExpireMinutes { get; init; } = 1440;

    // Which frontend origins are allowed to call this API from the browser
    // (CORS), as a comma-separated string turned into a list.
    public string CorsOrigins { get; init; } = "http://localhost:5173";

    public string[] CorsOriginList =>
        CorsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
