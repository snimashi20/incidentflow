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
using IncidentFlow.Api.Security;
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

    // Whether this test class wants the 75 demo employees from
    // Data/SeedData.cs in its database. Off for almost every test (see
    // below); SeedDataTests overrides it to `true` because checking the
    // seeded company is the entire point of that class.
    protected virtual bool SeedDemoEmployees => false;

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
            // By default, skip the 75 demo employees from Data/SeedData.cs.
            // The real departments ARE still seeded (the app needs them,
            // and tests need one to register against), but a test that
            // asserts on "the list of users" should see only the users it
            // created itself.
            builder.UseSetting("Seed:DemoEmployees", SeedDemoEmployees ? "true" : "false");

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

                // WebSocket tickets normally live in Redis, which isn't
                // running here either. The in-memory store keeps the same
                // contract - 30 second lifetime, single use - just scoped to
                // this one process. See Security/WebSocketTicketStore.cs.
                services.RemoveAll<IWebSocketTicketStore>();
                services.AddSingleton<IWebSocketTicketStore, InMemoryWebSocketTicketStore>();
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

    // Opening a WebSocket needs a short-lived ticket rather than the access
    // token (see Security/WebSocketTicketStore.cs for why). This does what
    // the frontend does: ask for one with the token in a header, and hand
    // back the throwaway credential that goes in the socket URL.
    protected async Task<string> WebSocketTicketAsync(string token)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/api/auth/ws-ticket", null);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("ticket").GetString()!;
    }

    // Every user must belong to a department, so tests need a real
    // department id to register against. The departments themselves are
    // always seeded (see Data/DatabaseSeeder.cs), so we just ask the API
    // for the first one.
    protected async Task<int> FirstDepartmentIdAsync()
    {
        var response = await Client.GetAsync("/api/departments");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body[0].GetProperty("id").GetInt32();
    }

    // Shortcut helper: registers a new user and logs them in, returning the
    // access token ready to attach to any authenticated request. Saves every
    // test from repeating the same two calls.
    // `seniority` is null for the overwhelming majority of tests, which just
    // need "some logged-in user" and get the registration default (MID).
    // Tests about who is allowed to see what pass an explicit level instead,
    // since that is exactly what the rule in Common/IncidentAccess.cs reads.
    protected async Task<string> RegisterAndLoginAsync(
        string email, string name = "Test User", string password = "supersecret123", string? seniority = null
    )
    {
        var departmentId = await FirstDepartmentIdAsync();
        await Client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, name, password, department_id = departmentId, seniority },
            Json
        );
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { email, password }, Json);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("access_token").GetString()!;
    }

    protected async Task<HttpClient> AuthenticatedClientAsync(
        string email, string name = "Test User", string password = "supersecret123", string? seniority = null
    )
    {
        var token = await RegisterAndLoginAsync(email, name, password, seniority);
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
