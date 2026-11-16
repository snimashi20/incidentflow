// Tests that the task endpoints respect the same visibility rule as
// everything else.
//
// These cover what used to be the widest hole in the app: /api/tasks/{id}
// and /api/incidents/{id}/tasks checked that you were logged in and nothing
// more, so any account could add work to - or edit work on - an incident it
// was never allowed to open. The create route was the worse of the two,
// because assigning yourself a task is one of the ways an incident becomes
// visible to you: it was a self-service access grant.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class TaskAccessTests : IntegrationTestBase
{
    private async Task<int> CreateIncidentAsync(HttpClient client, string title = "Private incident")
    {
        var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new { title, description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() },
            Json
        );
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
    }

    private static async Task<int> UserIdAsync(HttpClient client)
    {
        var me = await (await client.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>(Json);
        return me.GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task CreateTask_OnAnIncidentYouCannotSee_IsForbidden()
    {
        using var ownerClient = await AuthenticatedClientAsync("task.access.owner@example.com", "Owner");
        using var strangerClient = await AuthenticatedClientAsync("task.access.stranger@example.com", "Stranger");

        var incidentId = await CreateIncidentAsync(ownerClient);

        var response = await strangerClient.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/tasks", new { title = "Not my business" }, Json
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // The refusal actually protected the incident - no task was created.
        var detail = await (await ownerClient.GetAsync($"/api/incidents/{incidentId}")).Content
            .ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(0, detail.GetProperty("tasks").GetArrayLength());
    }

    [Fact]
    public async Task CreateTask_CannotBeUsedToGrantYourselfAccess()
    {
        // The attack this closes, start to finish: add a task to somebody
        // else's incident, assign it to yourself, and the visibility rule
        // then says the incident is yours to read.
        using var ownerClient = await AuthenticatedClientAsync("grant.owner@example.com", "Grant Owner");
        using var intruderClient = await AuthenticatedClientAsync("intruder@example.com", "Intruder");

        var incidentId = await CreateIncidentAsync(ownerClient, "Confidential outage");
        var intruderId = await UserIdAsync(intruderClient);

        var attempt = await intruderClient.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/tasks",
            new { title = "Let me in", assigned_to = intruderId },
            Json
        );
        Assert.Equal(HttpStatusCode.Forbidden, attempt.StatusCode);

        // Still cannot read it, which is the thing that actually matters.
        var read = await intruderClient.GetAsync($"/api/incidents/{incidentId}");
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
    }

    [Fact]
    public async Task UpdateTask_OnAnIncidentYouCannotSee_IsForbidden()
    {
        using var ownerClient = await AuthenticatedClientAsync("edit.owner@example.com", "Edit Owner");
        using var strangerClient = await AuthenticatedClientAsync("edit.stranger@example.com", "Edit Stranger");

        var incidentId = await CreateIncidentAsync(ownerClient);
        var created = await (await ownerClient.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/tasks", new { title = "Real work" }, Json
        )).Content.ReadFromJsonAsync<JsonElement>(Json);
        var taskId = created.GetProperty("id").GetInt32();

        // Guessing a task id is easy - they are sequential integers - so this
        // check is the only thing standing between a stranger and someone
        // else's work.
        var response = await strangerClient.PatchAsJsonAsync(
            $"/api/tasks/{taskId}", new { status = "DONE", title = "vandalised" }, Json
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Nothing was changed.
        var detail = await (await ownerClient.GetAsync($"/api/incidents/{incidentId}")).Content
            .ReadFromJsonAsync<JsonElement>(Json);
        var task = detail.GetProperty("tasks")[0];
        Assert.Equal("Real work", task.GetProperty("title").GetString());
        Assert.Equal("TODO", task.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Participants_CanStillAddAndEditTasks()
    {
        // The rule has to let the actual responders work. Somebody who joined
        // the incident is a responder.
        using var ownerClient = await AuthenticatedClientAsync("joiner.owner@example.com", "Joiner Owner");
        using var helperClient = await AuthenticatedClientAsync("helper@example.com", "Helper");

        var incidentId = await CreateIncidentAsync(ownerClient);
        await helperClient.PostAsync($"/api/incidents/{incidentId}/join", null);

        var created = await helperClient.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/tasks", new { title = "I can help with this" }, Json
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var taskId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        var updated = await helperClient.PatchAsJsonAsync($"/api/tasks/{taskId}", new { status = "DONE" }, Json);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
    }

    [Fact]
    public async Task Leaders_CanAddAndEditTasksOnAnyIncident()
    {
        // Managers and above can see every incident (IncidentAccess.LeaderLevel),
        // so they can act on every incident too - otherwise oversight would
        // mean being able to read a problem and not help with it.
        using var ownerClient = await AuthenticatedClientAsync("quiet.team@example.com", "Quiet Team");
        using var directorClient = await AuthenticatedClientAsync(
            "task.director@example.com", "Task Director", seniority: "DIRECTOR"
        );

        var incidentId = await CreateIncidentAsync(ownerClient);

        var response = await directorClient.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/tasks", new { title = "Pulling rank to help" }, Json
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task CreateTask_ForAMissingIncident_IsStill404()
    {
        // The access check must not turn "no such incident" into a 403 - a
        // 404 is the honest answer when there is nothing there at all.
        using var client = await AuthenticatedClientAsync("missing.incident@example.com", "Missing");

        var response = await client.PostAsJsonAsync(
            "/api/incidents/999999/tasks", new { title = "Into the void" }, Json
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
