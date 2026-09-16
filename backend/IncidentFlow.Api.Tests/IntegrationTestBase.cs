// The core idea: we want to test our real ASP.NET Core app and real endpoint
// logic, but WITHOUT needing a real Postgres server or a real Redis server
// running. So every test class below inherits from this base, which swaps
// both out for lightweight stand-ins just for the test run:
//   - Postgres -> SQLite running entirely in memory.
//   - Redis pub/sub -> LocalEventPublisher, which skips the network hop and
//     broadcasts straight to local WebSocket connections.
//
// IAsyncLifetime's InitializeAsync/DisposeAsync run before/after EVERY test
// method (xUnit creates a fresh instance of the test class per test by
// default), which is what gives each test its own empty database.
using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using IncidentFlow.Api.Common;
using IncidentFlow.Api.Data;
using IncidentFlow.Api.Realtime;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Xunit;

namespace IncidentFlow.Api.Tests;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    // Every JSON call in these tests needs to agree with the server's
    // snake_case field names (see IncidentFlow.Api/Common/JsonConfig.cs) -
    // otherwise e.g. reading "created_at" back into a CreatedAt property
    // would silently deserialize to the type's default value instead.
    protected static readonly JsonSerializerOptions Json = JsonConfig.Options;

    private DbConnection _connection = null!;
    private WebApplicationFactory<Program> _factory = null!;

    protected HttpClient Client { get; private set; } = null!;

    protected WebApplicationFactory<Program> Factory => _factory;

    public async Task InitializeAsync()
    {
        // A single open SQLite connection to ":memory:", kept alive for the
        // whole test - without keeping one connection open, each new
        // connection to ":memory:" would get its own empty database, which
        // isn't what we want.
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

                // Don't attempt to connect to a real Redis server during
                // tests, and don't start the background listener that
                // forwards Redis messages to sockets - IncidentEventPublisher
                // below replaces the whole hop.
                services.RemoveAll<IConnectionMultiplexer>();
                services.RemoveAll<IHostedService>();

                services.RemoveAll<IEventPublisher>();
                services.AddSingleton<IEventPublisher, LocalEventPublisher>();
            });
        });

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _factory.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // Shortcut helper: registers a new user and logs them in, returning the
    // access token ready to attach to any authenticated request. Saves every
    // test from repeating the same two calls.
    protected async Task<string> RegisterAndLoginAsync(
        string email, string name = "Test User", string password = "supersecret123"
    )
    {
        await Client.PostAsJsonAsync("/api/auth/register", new { email, name, password }, Json);
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { email, password }, Json);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("access_token").GetString()!;
    }

    protected async Task<HttpClient> AuthenticatedClientAsync(
        string email, string name = "Test User", string password = "supersecret123"
    )
    {
        var token = await RegisterAndLoginAsync(email, name, password);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
