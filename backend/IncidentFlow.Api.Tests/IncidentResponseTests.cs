// Tests for the three things that happen to an incident once it exists and
// somebody has to actually respond to it: acknowledging it, escalating it to
// the department's senior people, and naming an Incident Commander.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class IncidentResponseTests : IntegrationTestBase
{
    private async Task<int> CreateIncidentAsync(HttpClient client, string title = "Something broke", string severity = "HIGH")
    {
        var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new { title, description = "", severity, department_id = await FirstDepartmentIdAsync() },
            Json
        );
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
    }

    // Parses an ISO timestamp to the tick it represents, ignoring whether
    // the text carried a "Z". RoundtripKind keeps the digits exactly as
    // written instead of shifting them into local time.
    private static long ParseInstant(string? text) =>
        DateTime.Parse(text!, null, System.Globalization.DateTimeStyles.RoundtripKind).Ticks;

    private static async Task<int> UserIdAsync(HttpClient client)
    {
        var me = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>(Json);
        return me.GetProperty("id").GetInt32();
    }

    // ---- Acknowledge ------------------------------------------------------

    [Fact]
    public async Task Acknowledge_StampsTheClockAndLogsIt()
    {
        using var client = await AuthenticatedClientAsync("acker@example.com", "Acker");
        var incidentId = await CreateIncidentAsync(client);

        // A brand new incident has not been acknowledged by anyone.
        var before = await (await client.GetAsync($"/api/incidents/{incidentId}")).Content
            .ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(JsonValueKind.Null, before.GetProperty("acknowledged_at").ValueKind);

        var response = await client.PostAsync($"/api/incidents/{incidentId}/acknowledge", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("acknowledged_at").ValueKind);

        var logs = body.GetProperty("activity_logs").EnumerateArray();
        Assert.Contains(logs, log => log.GetProperty("message").GetString()!.Contains("acknowledged the incident"));
    }

    [Fact]
    public async Task Acknowledge_IsIdempotent_AndKeepsTheFirstResponder()
    {
        using var firstClient = await AuthenticatedClientAsync("first.responder@example.com", "First Responder");
        using var secondClient = await AuthenticatedClientAsync("second.responder@example.com", "Second Responder");

        var incidentId = await CreateIncidentAsync(firstClient);
        await secondClient.PostAsync($"/api/incidents/{incidentId}/join", null);

        var first = await (await firstClient.PostAsync($"/api/incidents/{incidentId}/acknowledge", null)).Content
            .ReadFromJsonAsync<JsonElement>(Json);
        var firstAt = first.GetProperty("acknowledged_at").GetString();

        // Somebody else clicking it afterwards must not overwrite the
        // original timestamp - MTTA is measured from the FIRST response.
        var second = await (await secondClient.PostAsync($"/api/incidents/{incidentId}/acknowledge", null)).Content
            .ReadFromJsonAsync<JsonElement>(Json);
        var secondAt = second.GetProperty("acknowledged_at").GetString();

        // Compared as instants rather than as text, because the two
        // responses can spell the SAME moment differently under SQLite:
        // the first reply serializes the DateTime still in memory (which
        // knows it is UTC, so it ends in "Z"), while the second reads it
        // back out of the database, and SQLite has nowhere to store that
        // "this is UTC" flag. Postgres keeps it, so this only bites in
        // tests - but comparing instants is the right assertion either way,
        // since the claim being tested is "same moment", not "same string".
        Assert.Equal(ParseInstant(firstAt), ParseInstant(secondAt));
    }

    [Fact]
    public async Task Acknowledge_PutsYouOnTheIncident()
    {
        using var ownerClient = await AuthenticatedClientAsync("ack.owner@example.com", "Ack Owner");
        using var helperClient = await AuthenticatedClientAsync("ack.helper@example.com", "Ack Helper");

        var incidentId = await CreateIncidentAsync(ownerClient);
        await helperClient.PostAsync($"/api/incidents/{incidentId}/join", null);
        await helperClient.PostAsync($"/api/incidents/{incidentId}/acknowledge", null);

        var detail = await (await helperClient.GetAsync($"/api/incidents/{incidentId}")).Content
            .ReadFromJsonAsync<JsonElement>(Json);

        Assert.Contains(
            detail.GetProperty("participants").EnumerateArray(),
            p => p.GetProperty("email").GetString() == "ack.helper@example.com"
        );
    }

    // ---- Escalation -------------------------------------------------------

    [Fact]
    public async Task Escalate_PullsInTheDepartmentsSeniorPeople()
    {
        // Everyone registered by these tests lands in the same department
        // (FirstDepartmentIdAsync), which is exactly the setup escalation
        // needs: a junior reporting something, and seniors to hand it to.
        using var juniorClient = await AuthenticatedClientAsync(
            "junior.dev@example.com", "Junior Dev", seniority: "JUNIOR"
        );
        using var leadClient = await AuthenticatedClientAsync(
            "the.lead@example.com", "The Lead", seniority: "LEAD"
        );
        using var midClient = await AuthenticatedClientAsync(
            "mid.dev@example.com", "Mid Dev", seniority: "MID"
        );

        var incidentId = await CreateIncidentAsync(juniorClient, "Production database is down", "CRITICAL");

        var response = await juniorClient.PostAsync($"/api/incidents/{incidentId}/escalate", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("escalated_at").ValueKind);

        var participantEmails = body.GetProperty("participants").EnumerateArray()
            .Select(p => p.GetProperty("email").GetString()).ToHashSet();

        // The tech lead gets pulled in...
        Assert.Contains("the.lead@example.com", participantEmails);
        // ...and the mid-level engineer does not: escalating means reaching
        // for someone senior, not paging the whole department.
        Assert.DoesNotContain("mid.dev@example.com", participantEmails);

        var logs = body.GetProperty("activity_logs").EnumerateArray();
        Assert.Contains(logs, log => log.GetProperty("message").GetString()!.Contains("escalated the incident"));
    }

    [Fact]
    public async Task Escalate_GivesTheSeniorPersonAccess()
    {
        using var juniorClient = await AuthenticatedClientAsync(
            "quiet.junior@example.com", "Quiet Junior", seniority: "JUNIOR"
        );
        using var leadClient = await AuthenticatedClientAsync(
            "escalation.lead@example.com", "Escalation Lead", seniority: "LEAD"
        );

        var incidentId = await CreateIncidentAsync(juniorClient, "Needs a grown-up");

        // A LEAD is below IncidentAccess.LeaderLevel, so they cannot see
        // other people's incidents by default - before escalation this is
        // none of their business.
        var before = await leadClient.GetAsync($"/api/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.Forbidden, before.StatusCode);

        await juniorClient.PostAsync($"/api/incidents/{incidentId}/escalate", null);

        // Escalation is what hands it to them - being added as a participant
        // is what grants the access.
        var after = await leadClient.GetAsync($"/api/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    // ---- Incident Commander -----------------------------------------------

    [Fact]
    public async Task SetCommander_NamesAnOwnerAndAddsThemToTheIncident()
    {
        using var reporterClient = await AuthenticatedClientAsync("reporter@example.com", "Reporter");
        using var commanderClient = await AuthenticatedClientAsync("commander@example.com", "Commander");

        var incidentId = await CreateIncidentAsync(reporterClient);
        var commanderId = await UserIdAsync(commanderClient);

        var response = await reporterClient.PatchAsJsonAsync(
            $"/api/incidents/{incidentId}/commander", new { user_id = commanderId }, Json
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        Assert.Equal(commanderId, body.GetProperty("commander_user_id").GetInt32());
        Assert.Equal("Commander", body.GetProperty("commander").GetProperty("name").GetString());

        // Being put in charge of an incident has to come with access to it.
        Assert.Contains(
            body.GetProperty("participants").EnumerateArray(),
            p => p.GetProperty("email").GetString() == "commander@example.com"
        );

        var logs = body.GetProperty("activity_logs").EnumerateArray();
        Assert.Contains(logs, log => log.GetProperty("message").GetString()!.Contains("incident commander"));
    }

    [Fact]
    public async Task SetCommander_ToYourself_ReadsAsTakingCommand()
    {
        using var client = await AuthenticatedClientAsync("self.commander@example.com", "Self Commander");
        var incidentId = await CreateIncidentAsync(client);
        var myId = await UserIdAsync(client);

        var body = await (await client.PatchAsJsonAsync(
            $"/api/incidents/{incidentId}/commander", new { user_id = myId }, Json
        )).Content.ReadFromJsonAsync<JsonElement>(Json);

        var logs = body.GetProperty("activity_logs").EnumerateArray();
        Assert.Contains(logs, log => log.GetProperty("message").GetString()!.Contains("took command"));
    }

    [Fact]
    public async Task ClearCommander_RemovesTheRole()
    {
        using var client = await AuthenticatedClientAsync("standdown@example.com", "Stand Down");
        var incidentId = await CreateIncidentAsync(client);
        var myId = await UserIdAsync(client);

        await client.PatchAsJsonAsync($"/api/incidents/{incidentId}/commander", new { user_id = myId }, Json);

        var cleared = await client.PatchAsJsonAsync(
            $"/api/incidents/{incidentId}/commander", new { user_id = (int?)null }, Json
        );

        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var body = await cleared.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("commander_user_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("commander").ValueKind);
    }

    [Fact]
    public async Task SetCommander_RejectsAUserWhoDoesNotExist()
    {
        using var client = await AuthenticatedClientAsync("ghost.commander@example.com", "Ghost Commander");
        var incidentId = await CreateIncidentAsync(client);

        var response = await client.PatchAsJsonAsync(
            $"/api/incidents/{incidentId}/commander", new { user_id = 999999 }, Json
        );

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ---- Resolution timestamps -------------------------------------------

    [Fact]
    public async Task Resolving_StampsResolvedAt_AndReopeningClearsIt()
    {
        using var client = await AuthenticatedClientAsync("resolver@example.com", "Resolver");
        var incidentId = await CreateIncidentAsync(client);

        var resolved = await (await client.PatchAsJsonAsync(
            $"/api/incidents/{incidentId}/status", new { status = "RESOLVED" }, Json
        )).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.NotEqual(JsonValueKind.Null, resolved.GetProperty("resolved_at").ValueKind);

        // Reopening has to clear it, otherwise "time to resolve" would be
        // measured against a moment the incident was not actually resolved.
        var reopened = await (await client.PatchAsJsonAsync(
            $"/api/incidents/{incidentId}/status", new { status = "INVESTIGATING" }, Json
        )).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("resolved_at").ValueKind);
    }
}
