// Tests for creating/listing/viewing incidents, changing status, and joining.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class IncidentEndpointsTests : IntegrationTestBase
{
    [Fact]
    public async Task CreateIncident()
    {
        using var client = await AuthenticatedClientAsync("owner@example.com", "Owner");

        var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new { title = "Payment API Failure", description = "500s on checkout", severity = "HIGH", department_id = await FirstDepartmentIdAsync() },
            Json
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Payment API Failure", body.GetProperty("title").GetString());
        Assert.Equal("OPEN", body.GetProperty("status").GetString()); // every new incident starts OPEN

        // The creator should be automatically added as a participant...
        var participants = body.GetProperty("participants");
        Assert.Equal(1, participants.GetArrayLength());
        Assert.Equal("owner@example.com", participants[0].GetProperty("email").GetString());

        // ...and get an activity log entry recorded for creating it.
        var logs = body.GetProperty("activity_logs").EnumerateArray();
        Assert.Contains(logs, log => log.GetProperty("message").GetString()!.Contains("created the incident"));
    }

    [Fact]
    public async Task CreateIncident_RequiresAuth()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/incidents", new { title = "X", description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() }, Json
        );
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListIncidents()
    {
        using var client = await AuthenticatedClientAsync("lister@example.com", "Lister");

        await client.PostAsJsonAsync("/api/incidents", new { title = "Incident A", description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() }, Json);
        await client.PostAsJsonAsync("/api/incidents", new { title = "Incident B", description = "", severity = "MEDIUM", department_id = await FirstDepartmentIdAsync() }, Json);

        var response = await client.GetAsync("/api/incidents");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var titles = body.EnumerateArray().Select(i => i.GetProperty("title").GetString()).ToHashSet();
        Assert.Contains("Incident A", titles);
        Assert.Contains("Incident B", titles);
    }

    [Fact]
    public async Task UpdateStatus_LogsActivity()
    {
        using var client = await AuthenticatedClientAsync("responder@example.com", "Responder");

        var createResponse = await client.PostAsJsonAsync(
            "/api/incidents", new { title = "DB outage", description = "", severity = "CRITICAL", department_id = await FirstDepartmentIdAsync() }, Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var response = await client.PatchAsJsonAsync(
            $"/api/incidents/{incidentId}/status", new { status = "INVESTIGATING" }, Json
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("INVESTIGATING", body.GetProperty("status").GetString());

        // Confirms the human-readable "before -> after" message got recorded.
        var logs = body.GetProperty("activity_logs").EnumerateArray();
        Assert.Contains(logs, log => log.GetProperty("message").GetString()!.Contains("OPEN → INVESTIGATING"));
    }

    [Fact]
    public async Task JoinIncident_AddsParticipantOnce()
    {
        using var ownerClient = await AuthenticatedClientAsync("creator@example.com", "Creator");
        using var joinerClient = await AuthenticatedClientAsync("joiner@example.com", "Joiner");

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/incidents", new { title = "Cache down", description = "", severity = "MEDIUM", department_id = await FirstDepartmentIdAsync() }, Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        // Join the same incident twice as the same user...
        var response1 = await joinerClient.PostAsync($"/api/incidents/{incidentId}/join", null);
        var response2 = await joinerClient.PostAsync($"/api/incidents/{incidentId}/join", null);

        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        // ...and confirm they only show up once in the participants list, not twice.
        var body = await response2.Content.ReadFromJsonAsync<JsonElement>(Json);
        var joinerCount = body.GetProperty("participants").EnumerateArray()
            .Count(p => p.GetProperty("email").GetString() == "joiner@example.com");
        Assert.Equal(1, joinerCount);
    }

    [Fact]
    public async Task GetMissingIncident_404()
    {
        using var client = await AuthenticatedClientAsync("ghost@example.com", "Ghost");
        var response = await client.GetAsync("/api/incidents/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Visibility: who can see which incidents -------------------------

    [Fact]
    public async Task ListIncidents_HidesIncidentsYouAreNotInvolvedIn()
    {
        using var ownerClient = await AuthenticatedClientAsync("hidden.owner@example.com", "Hidden Owner");
        using var strangerClient = await AuthenticatedClientAsync("stranger@example.com", "Stranger");

        await ownerClient.PostAsJsonAsync(
            "/api/incidents", new { title = "Private incident", description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() }, Json
        );

        // The creator sees it...
        var ownerList = await (await ownerClient.GetAsync("/api/incidents")).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains(ownerList.EnumerateArray(), i => i.GetProperty("title").GetString() == "Private incident");

        // ...and an uninvolved person doesn't, even though it exists.
        var strangerList = await (await strangerClient.GetAsync("/api/incidents")).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.DoesNotContain(strangerList.EnumerateArray(), i => i.GetProperty("title").GetString() == "Private incident");
    }

    [Fact]
    public async Task GetIncident_IsForbiddenForSomeoneNotInvolved()
    {
        using var ownerClient = await AuthenticatedClientAsync("detail.owner@example.com", "Detail Owner");
        using var strangerClient = await AuthenticatedClientAsync("detail.stranger@example.com", "Detail Stranger");

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/incidents", new { title = "Not for you", description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() }, Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        // Hiding it from the list isn't enough - reading it directly by id
        // has to be refused too, or the rule is decoration.
        var response = await strangerClient.GetAsync($"/api/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task BeingAssignedATask_MakesTheIncidentVisible()
    {
        using var ownerClient = await AuthenticatedClientAsync("task.owner@example.com", "Task Owner");
        using var workerClient = await AuthenticatedClientAsync("task.worker@example.com", "Task Worker");

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/incidents", new { title = "Needs a specialist", description = "", severity = "HIGH", department_id = await FirstDepartmentIdAsync() }, Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        // Before being given any work, the incident is invisible to them.
        var before = await (await workerClient.GetAsync("/api/incidents")).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.DoesNotContain(before.EnumerateArray(), i => i.GetProperty("id").GetInt32() == incidentId);

        var workerId = (await (await workerClient.GetAsync("/api/auth/me"))
            .Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        await ownerClient.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/tasks", new { title = "Look at the logs", assigned_to = workerId }, Json
        );

        // Being handed a task is what grants access - otherwise you could be
        // assigned work on an incident you aren't allowed to open.
        var after = await (await workerClient.GetAsync("/api/incidents")).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains(after.EnumerateArray(), i => i.GetProperty("id").GetInt32() == incidentId);

        var detail = await workerClient.GetAsync($"/api/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task Leaders_SeeEveryIncident()
    {
        using var ownerClient = await AuthenticatedClientAsync("quiet.owner@example.com", "Quiet Owner");
        // Registering at DIRECTOR puts this account above IncidentAccess.LeaderLevel.
        using var leaderClient = await AuthenticatedClientAsync(
            "the.director@example.com", "The Director", seniority: "DIRECTOR"
        );

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/incidents", new { title = "Someone elses problem", description = "", severity = "MEDIUM", department_id = await FirstDepartmentIdAsync() }, Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        // The leader never touched this incident, but oversight means seeing it.
        var list = await (await leaderClient.GetAsync("/api/incidents")).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains(list.EnumerateArray(), i => i.GetProperty("id").GetInt32() == incidentId);

        var detail = await leaderClient.GetAsync($"/api/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
    }

    [Fact]
    public async Task ATechLead_IsNotTreatedAsALeader()
    {
        using var ownerClient = await AuthenticatedClientAsync("lead.owner@example.com", "Lead Owner");
        // LEAD sits just below the MANAGER cut-off on purpose: a tech lead is
        // a hands-on engineer, not someone with company-wide oversight.
        using var leadClient = await AuthenticatedClientAsync(
            "tech.lead@example.com", "Tech Lead", seniority: "LEAD"
        );

        await ownerClient.PostAsJsonAsync(
            "/api/incidents", new { title = "Nothing to do with the lead", description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() }, Json
        );

        var list = await (await leadClient.GetAsync("/api/incidents")).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.DoesNotContain(
            list.EnumerateArray(), i => i.GetProperty("title").GetString() == "Nothing to do with the lead"
        );
    }

    [Fact]
    public async Task ListIncidents_OrdersOpenThenInvestigatingThenResolved()
    {
        using var client = await AuthenticatedClientAsync("sorter@example.com", "Sorter", seniority: "DIRECTOR");

        // Created oldest-first; the statuses below are what should decide the
        // final order, NOT the order they were created in.
        var resolvedId = await CreateIncidentAsync(client, "Should be last");
        var openId = await CreateIncidentAsync(client, "Should be first");
        var investigatingId = await CreateIncidentAsync(client, "Should be middle");

        await client.PatchAsJsonAsync($"/api/incidents/{resolvedId}/status", new { status = "RESOLVED" }, Json);
        await client.PatchAsJsonAsync($"/api/incidents/{investigatingId}/status", new { status = "INVESTIGATING" }, Json);

        var list = await (await client.GetAsync("/api/incidents")).Content.ReadFromJsonAsync<JsonElement>(Json);
        var ids = list.EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToList();

        // Guards against the trap that the status column stores TEXT, so a
        // naive sort would give INVESTIGATING, OPEN, RESOLVED alphabetically.
        Assert.Equal(new[] { openId, investigatingId, resolvedId }, ids);
    }

    private async Task<int> CreateIncidentAsync(HttpClient client, string title)
    {
        var response = await client.PostAsJsonAsync(
            "/api/incidents", new { title, description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() }, Json
        );
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task DeleteIncident_RemovesItAndItsChildren()
    {
        using var client = await AuthenticatedClientAsync("deleter@example.com", "Deleter");

        var createResponse = await client.PostAsJsonAsync(
            "/api/incidents", new { title = "Duplicate incident", description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() }, Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        // Give it a task, so we're also proving the cascade delete works and
        // doesn't trip over a child row still pointing at the incident.
        await client.PostAsJsonAsync($"/api/incidents/{incidentId}/tasks", new { title = "Some task" }, Json);

        var response = await client.DeleteAsync($"/api/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // It's really gone, not just hidden from the list.
        var getResponse = await client.GetAsync($"/api/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        var listBody = await (await client.GetAsync("/api/incidents")).Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.DoesNotContain(listBody.EnumerateArray(), i => i.GetProperty("id").GetInt32() == incidentId);
    }

    [Fact]
    public async Task DeleteIncident_OnlyCreatorIsAllowed()
    {
        using var ownerClient = await AuthenticatedClientAsync("owner2@example.com", "Owner Two");
        using var otherClient = await AuthenticatedClientAsync("intruder@example.com", "Intruder");

        var createResponse = await ownerClient.PostAsJsonAsync(
            "/api/incidents", new { title = "Not yours", description = "", severity = "HIGH", department_id = await FirstDepartmentIdAsync() }, Json
        );
        var incidentId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        // 403 Forbidden, not 404 - we know who they are, they just can't do this.
        var response = await otherClient.DeleteAsync($"/api/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // And the refusal actually protected the data.
        var getResponse = await ownerClient.GetAsync($"/api/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteIncident_RequiresAuth()
    {
        var response = await Client.DeleteAsync("/api/incidents/1");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteMissingIncident_404()
    {
        using var client = await AuthenticatedClientAsync("ghost2@example.com", "Ghost Two");
        var response = await client.DeleteAsync("/api/incidents/999999");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
