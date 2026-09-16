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
using IncidentFlow.Api.Security;
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
    JwtSecret = builder.Configuration["Jwt:Secret"] ?? "dev-secret-change-me",
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

// Starts alongside the web server and runs for its whole lifetime, forwarding
// Redis Pub/Sub messages to locally-connected WebSocket clients - see
// Realtime/RedisListenerBackgroundService.cs.
builder.Services.AddHostedService<RedisListenerBackgroundService>();

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
});

// Each of these groups related endpoints together (auth, incidents, tasks,
// ...) so this file doesn't turn into one giant list of routes.
app.MapAuthEndpoints();
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
    await db.Database.EnsureCreatedAsync();
}

app.Run();

// Minimal APIs normally don't need a named Program class - top-level
// statements generate one implicitly. We declare it explicitly (as `partial`,
// so the compiler merges it with the generated one) purely so the test
// project can reference `Program` when spinning up an in-memory copy of this
// app via WebApplicationFactory<Program> - see IncidentFlow.Api.Tests.
public partial class Program { }
