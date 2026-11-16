// Tests for creating tasks under an incident and updating their status/assignee.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class TaskEndpointsTests : IntegrationTestBase
{
    // Small helper so every test below doesn't have to repeat the same
    // "create an incident first" boilerplate.
    private async Task<int> CreateIncidentAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/incidents", new { title = "API down", description = "", severity = "HIGH", department_id = await FirstDepartmentIdAsync() }, Json
        );
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        return body.GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task CreateTask()
    {
        using var client = await AuthenticatedClientAsync("taskowner@example.com", "Task Owner");
        var incidentId = await CreateIncidentAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/tasks", new { title = "Check API logs" }, Json
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Check API logs", body.GetProperty("title").GetString());
        Assert.Equal("TODO", body.GetProperty("status").GetString()); // every new task starts as TODO
        Assert.Equal(incidentId, body.GetProperty("incident_id").GetInt32());
    }

    [Fact]
    public async Task CreateTask_ForMissingIncident_404()
    {
        using var client = await AuthenticatedClientAsync("nope@example.com", "Nope");
        var response = await client.PostAsJsonAsync("/api/incidents/999999/tasks", new { title = "X" }, Json);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateTaskStatus()
    {
        using var client = await AuthenticatedClientAsync("updater@example.com", "Updater");
        var incidentId = await CreateIncidentAsync(client);
        var createResponse = await client.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/tasks", new { title = "Restart service" }, Json
        );
        var taskId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var response = await client.PatchAsJsonAsync($"/api/tasks/{taskId}", new { status = "IN_PROGRESS" }, Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("IN_PROGRESS", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task AssignTaskToUser()
    {
        using var assignerClient = await AuthenticatedClientAsync("assigner@example.com", "Assigner");
        using var assigneeClient = await AuthenticatedClientAsync("assignee@example.com", "Assignee");

        var incidentId = await CreateIncidentAsync(assignerClient);
        var createResponse = await assignerClient.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/tasks", new { title = "Contact provider" }, Json
        );
        var taskId = (await createResponse.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        // Look up the assignee's user id via the /api/users list endpoint,
        // just like the real frontend does when populating the "assign to"
        // dropdown.
        var usersResponse = await assignerClient.GetAsync("/api/users");
        var users = await usersResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        var assigneeId = users.EnumerateArray()
            .First(u => u.GetProperty("email").GetString() == "assignee@example.com")
            .GetProperty("id").GetInt32();

        // The ASSIGNER hands the work out - which is both how it happens in
        // the UI and the only way it is allowed now.
        //
        // This used to be sent as the assignee, who at that point was not on
        // the incident at all. That worked only because the task endpoints
        // did not check visibility: it let somebody with no connection to an
        // incident assign themselves work on it, and thereby grant themselves
        // access to it. See RequireVisibleIncidentAsync in TaskEndpoints.cs.
        var response = await assignerClient.PatchAsJsonAsync(
            $"/api/tasks/{taskId}", new { assigned_to = assigneeId }, Json
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(assigneeId, body.GetProperty("assigned_to").GetInt32());

        // And now that they hold the task, the assignee can work on it
        // themselves - being given a task is what grants the access.
        var assigneeUpdate = await assigneeClient.PatchAsJsonAsync(
            $"/api/tasks/{taskId}", new { status = "IN_PROGRESS" }, Json
        );
        Assert.Equal(HttpStatusCode.OK, assigneeUpdate.StatusCode);
    }
}
