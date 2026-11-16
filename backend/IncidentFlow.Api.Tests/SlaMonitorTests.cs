// Tests for the SLA rule and the job that applies it.
//
// These call SlaMonitor.RunOnceAsync directly with a made-up `now` rather
// than starting the background service and waiting. That is the entire
// reason the rule was split out from the timer (see
// Realtime/SlaMonitorBackgroundService.cs): "what happens two hours from
// now?" is a question a test can ask in milliseconds.
using System.Net.Http.Json;
using System.Text.Json;
using IncidentFlow.Api.Common;
using IncidentFlow.Api.Data;
using IncidentFlow.Api.Entities;
using IncidentFlow.Api.Realtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class SlaMonitorTests : IntegrationTestBase
{
    private async Task<int> CreateIncidentAsync(HttpClient client, string severity)
    {
        var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new { title = $"{severity} incident", description = "", severity, department_id = await FirstDepartmentIdAsync() },
            Json
        );
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
    }

    // Runs the monitor against the same database the HTTP tests are using.
    // AppDbContext is a scoped service, so it has to be resolved from a
    // scope rather than straight off the root provider.
    private async Task<List<Incident>> RunMonitorAsync(DateTime now)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var events = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        return await SlaMonitor.RunOnceAsync(db, events, now);
    }

    [Fact]
    public void SlaBudgets_GetShorterAsSeverityRises()
    {
        // The policy itself, with no database involved: a CRITICAL incident
        // must be picked up faster than a LOW one. Worth asserting because
        // the numbers are easy to edit and easy to get backwards.
        Assert.True(SlaPolicy.MinutesFor(Severity.CRITICAL) < SlaPolicy.MinutesFor(Severity.HIGH));
        Assert.True(SlaPolicy.MinutesFor(Severity.HIGH) < SlaPolicy.MinutesFor(Severity.MEDIUM));
        Assert.True(SlaPolicy.MinutesFor(Severity.MEDIUM) < SlaPolicy.MinutesFor(Severity.LOW));
    }

    [Fact]
    public async Task UnacknowledgedIncident_IsFlaggedOnceItsBudgetRunsOut()
    {
        using var client = await AuthenticatedClientAsync("sla.victim@example.com", "SLA Victim");
        var incidentId = await CreateIncidentAsync(client, "CRITICAL"); // 15 minute budget

        // Five minutes in, still within budget - nothing should happen.
        var early = await RunMonitorAsync(DateTime.UtcNow.AddMinutes(5));
        Assert.Empty(early);

        // An hour in, with nobody having acknowledged it, it is late.
        var late = await RunMonitorAsync(DateTime.UtcNow.AddHours(1));
        Assert.Contains(late, i => i.Id == incidentId);

        var detail = await (await client.GetAsync($"/api/incidents/{incidentId}")).Content
            .ReadFromJsonAsync<JsonElement>(Json);
        Assert.NotEqual(JsonValueKind.Null, detail.GetProperty("sla_breached_at").ValueKind);
    }

    [Fact]
    public async Task AcknowledgedIncident_IsNeverFlagged()
    {
        using var client = await AuthenticatedClientAsync("sla.responder@example.com", "SLA Responder");
        var incidentId = await CreateIncidentAsync(client, "CRITICAL");

        await client.PostAsync($"/api/incidents/{incidentId}/acknowledge", null);

        // A whole day later it is still not a breach - somebody turned up.
        var breached = await RunMonitorAsync(DateTime.UtcNow.AddDays(1));

        Assert.DoesNotContain(breached, i => i.Id == incidentId);
    }

    [Fact]
    public async Task ResolvedIncident_IsNeverFlagged()
    {
        using var client = await AuthenticatedClientAsync("sla.fixer@example.com", "SLA Fixer");
        var incidentId = await CreateIncidentAsync(client, "CRITICAL");

        // Fixed without anyone clicking "acknowledge" first. Technically
        // nobody acknowledged it, but shouting about an SLA breach on an
        // incident that is already closed helps nobody.
        await client.PatchAsJsonAsync($"/api/incidents/{incidentId}/status", new { status = "RESOLVED" }, Json);

        var breached = await RunMonitorAsync(DateTime.UtcNow.AddDays(1));

        Assert.DoesNotContain(breached, i => i.Id == incidentId);
    }

    [Fact]
    public async Task AnIncidentIsOnlyFlaggedOnce()
    {
        using var client = await AuthenticatedClientAsync("sla.repeat@example.com", "SLA Repeat");
        var incidentId = await CreateIncidentAsync(client, "CRITICAL");

        var firstRun = await RunMonitorAsync(DateTime.UtcNow.AddHours(1));
        Assert.Contains(firstRun, i => i.Id == incidentId);

        // The monitor runs every minute forever. If it kept re-reporting the
        // same incident, every open page would get a breach event once a
        // minute for the rest of the incident's life.
        var secondRun = await RunMonitorAsync(DateTime.UtcNow.AddHours(2));
        Assert.DoesNotContain(secondRun, i => i.Id == incidentId);
    }

    [Fact]
    public async Task LowSeverityIncidents_GetALongerGrace()
    {
        using var client = await AuthenticatedClientAsync("sla.patient@example.com", "SLA Patient");
        var incidentId = await CreateIncidentAsync(client, "LOW"); // 24 hour budget

        // An hour in, a LOW incident is nowhere near late, even though a
        // CRITICAL one created at the same moment would already be flagged.
        var breached = await RunMonitorAsync(DateTime.UtcNow.AddHours(1));

        Assert.DoesNotContain(breached, i => i.Id == incidentId);
    }
}
