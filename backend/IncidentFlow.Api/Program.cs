// This is the entry point of the whole backend - the file `dotnet run`
// executes. It reads configuration, wires up dependency injection (the
// container that hands out things like the database connection and the JWT
// service to whichever endpoint needs them), builds the HTTP middleware
// pipeline, and maps every route from Endpoints/ (Minimal APIs group routes
// with extension methods, see MapAuthEndpoints() etc. below).
using IncidentFlow.Api.Common;
using IncidentFlow.Api.Data;
using IncidentFlow.Api.Endpoints;
using IncidentFlow.Api.Realtime;
using System.Threading.RateLimiting;
using IncidentFlow.Api.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration ----
// Reads from appsettings.json first, then environment variables override it -
// standard ASP.NET Core behavior. docker-compose.yml sets the environment
// variables in production; appsettings.json's values are just for local dev
// without Docker. This plays the same role config.py's Settings class did.
var appOptions = new AppOptions
{
    PostgresConnectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? "Host=localhost;Port=5432;Database=incidentflow;Username=incidentflow;Password=incidentflow",
    RedisConnectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379",
    JwtSecret = builder.Configuration["Jwt:Secret"] ?? "dev-secret-change-me-to-a-long-random-string",
    AccessTokenExpireMinutes = int.TryParse(builder.Configuration["Jwt:AccessTokenExpireMinutes"], out var minutes)
        ? minutes
        : 1440,
    CorsOrigins = builder.Configuration["Cors:Origins"] ?? "http://localhost:5173",
};
builder.Services.AddSingleton(appOptions);

// ---- Services (dependency injection container) ----
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(appOptions.PostgresConnectionString));

builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(appOptions.RedisConnectionString));
builder.Services.AddSingleton<JwtService>();
builder.Services.AddSingleton<IncidentSocketManager>();
builder.Services.AddSingleton<IEventPublisher, RedisEventPublisher>();

// Hands out the 30-second single-use tickets browsers use to open a
// WebSocket, so the access token never has to travel in a URL. Backed by
// Redis because the request that issues a ticket and the socket that
// redeems it can land on different replicas - see
// Security/WebSocketTicketStore.cs.
builder.Services.AddSingleton<IWebSocketTicketStore, RedisWebSocketTicketStore>();

// ---- Rate limiting ----
// Only /api/auth/login opts into this (see AuthEndpoints.cs). Deliberately
// NOT applied to the whole auth group: /api/auth/me is called on every page
// load, so throttling it would break normal use rather than any attack.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Without this, a throttled caller gets an empty 429 body, while every
    // other error in this app answers with {"detail": "..."} - see the
    // ApiException middleware below. One shape of error response is much
    // easier for a client to handle than two.
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { detail = "Too many login attempts. Please wait a minute and try again." },
            cancellationToken
        );
    };

    options.AddPolicy(RateLimitPolicies.Login, context => RateLimitPartition.GetFixedWindowLimiter(
        // One budget per client IP address. The fallback matters for the
        // in-memory test server, where there is no real remote address.
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = RateLimitPolicies.LoginPermitsPerWindow,
            Window = RateLimitPolicies.LoginWindow,
            // No queue: a refused login should be told so immediately, not
            // held open waiting for a slot. Queueing would turn a flood of
            // guesses into a pile of slow requests holding server resources.
            QueueLimit = 0,
        }
    ));
});

// Starts alongside the web server and runs for its whole lifetime, forwarding
// Redis Pub/Sub messages to locally-connected WebSocket clients - see
// Realtime/RedisListenerBackgroundService.cs.
builder.Services.AddHostedService<RedisListenerBackgroundService>();

// The other background job: once a minute, look for incidents nobody has
// acknowledged within their severity's time budget and flag them. See
// Realtime/SlaMonitorBackgroundService.cs - it is the only part of the app
// that acts without somebody clicking something first.
builder.Services.AddHostedService<SlaMonitorBackgroundService>();

// CORS (Cross-Origin Resource Sharing): by default browsers block a webpage
// on one origin (http://localhost:5173) from calling an API on another
// origin (http://localhost:8000). This explicitly allows our frontend's
// origin to call this API.
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(appOptions.CorsOriginList)
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());
});

// The frontend expects snake_case JSON field names (see Common/JsonConfig.cs)
// - this makes every JSON body this server reads or writes, anywhere in the
// app, use that same convention.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonConfig.Options.PropertyNamingPolicy;
    foreach (var converter in JsonConfig.Options.Converters)
    {
        options.SerializerOptions.Converters.Add(converter);
    }
});

var app = builder.Build();

app.UseCors("Frontend");

// Must come before the endpoints it protects, since it works by refusing
// requests on their way in.
app.UseRateLimiter();

// Turns on support for accepting WebSocket connections - required before our
// /ws/incidents/{id} route can call context.WebSockets.AcceptWebSocketAsync().
app.UseWebSockets();

// Global error handling: any ApiException thrown from an endpoint (see
// Common/ApiException.cs) becomes a {"detail": "..."} JSON response with
// the matching status code, instead
// of every endpoint having to build that response by hand.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ApiException ex)
    {
        context.Response.StatusCode = ex.StatusCode;
        if (ex.StatusCode == StatusCodes.Status401Unauthorized)
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
        }
        await context.Response.WriteAsJsonAsync(new { detail = ex.Detail });
    }
    // Raised by EF Core when an UPDATE matched no rows because somebody else
    // changed the same record first - see the xmin concurrency token in
    // Data/AppDbContext.cs. Handled here, once, rather than in every endpoint
    // that writes something: the answer is always the same, and a 500 would
    // tell the caller nothing useful about a situation where retrying works.
    catch (DbUpdateConcurrencyException ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Concurrency");
        logger.LogWarning(ex, "Concurrent update rejected on {Path}", context.Request.Path);

        context.Response.StatusCode = StatusCodes.Status409Conflict;
        await context.Response.WriteAsJsonAsync(new
        {
            detail = "Somebody else changed this at the same time. Reload the page and try again.",
        });
    }
});

// Each of these groups related endpoints together (auth, incidents, tasks,
// ...) so this file doesn't turn into one giant list of routes.
app.MapAuthEndpoints();
app.MapCommentEndpoints();
app.MapDepartmentEndpoints();
app.MapIncidentEndpoints();
app.MapTaskEndpoints();
app.MapUserEndpoints();
app.MapIncidentWebSocket();

// Simple endpoint to check the API is up - useful for Docker health checks
// and for quickly confirming the backend is reachable.
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// Creates all tables (if they don't already exist) on startup. This is a
// simplified stand-in for a real migration tool like EF Core Migrations -
// fine for a learning project, but a production app would use versioned
// migrations instead so schema changes are tracked and reversible.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    await db.Database.EnsureCreatedAsync();

    // Then fill in the company: the 14 departments (reference data the app
    // needs to work at all) and, unless switched off, the 75 demo employees
    // from Data/SeedData.cs. Seeding is idempotent, so this is safe to run
    // on every single startup - see Data/DatabaseSeeder.cs.
    //
    // Set Seed__DemoEmployees=false (an environment variable, or
    // "Seed": {"DemoEmployees": false} in appsettings.json) to skip the
    // demo people - they all share one well-known password, so you would
    // never want them anywhere real. Anything other than an explicit
    // "false" means yes, which keeps local development zero-config.
    var seedDemoEmployees = !bool.TryParse(builder.Configuration["Seed:DemoEmployees"], out var seedFlag) || seedFlag;

    try
    {
        await DatabaseSeeder.SeedAsync(db, seedDemoEmployees, logger);
    }
    // DbException is the provider-independent base class, so this catch
    // works the same for Postgres in Docker and for the SQLite database the
    // tests use.
    catch (System.Data.Common.DbException ex)
    {
        // Almost always means the database was created before departments
        // existed: EnsureCreatedAsync only creates tables when there are
        // none at all, so it leaves an older database untouched and
        // "Departments" is simply missing. A migration tool would handle
        // this properly; for this project, recreating the database is the
        // intended fix, so say so clearly instead of crashing cryptically.
        logger.LogError(
            ex,
            "Could not seed departments/employees. If this database was created before departments " +
            "existed, its schema is out of date - recreate it with `docker compose down -v` followed " +
            "by `docker compose up`."
        );
        throw;
    }
}

app.Run();

// Minimal APIs normally don't need a named Program class - top-level
// statements generate one implicitly. We declare it explicitly (as `partial`,
// so the compiler merges it with the generated one) purely so the test
// project can reference `Program` when spinning up an in-memory copy of this
// app via WebApplicationFactory<Program> - see IncidentFlow.Api.Tests.
public partial class Program { }
