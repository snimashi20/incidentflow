// Tests that a task can only ever be assigned to a real person.
//
// The create and update routes used to disagree about this: PATCH looked the
// user up (it needed their name for the activity line) while POST stored
// whatever integer arrived, so a task could be "assigned" to user 9999 and
// render as unassigned.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class TaskAssignmentTests : IntegrationTestBase
{
    private async Task<int> CreateIncidentAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new { title = "Assignment test", description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() },
            Json
        );
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task CreateTask_WithAnUnknownAssignee_IsRejected()
    {
        using var client = await AuthenticatedClientAsync("assign.create@example.com", "Assigner");
        var incidentId = await CreateIncidentAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/tasks", new { title = "Ghost work", assigned_to = 999999 }, Json
        );

        // 422, not a 500 from a foreign-key violation, and not a silently
        // created task pointing at nobody.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // And nothing was created.
        var detail = await (await client.GetAsync($"/api/incidents/{incidentId}")).Content
            .ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(0, detail.GetProperty("tasks").GetArrayLength());
    }

    [Fact]
    public async Task UpdateTask_WithAnUnknownAssignee_IsRejected()
    {
        using var client = await AuthenticatedClientAsync("assign.update@example.com", "Updater");
        var incidentId = await CreateIncidentAsync(client);

        var created = await (await client.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/tasks", new { title = "Real work" }, Json
        )).Content.ReadFromJsonAsync<JsonElement>(Json);
        var taskId = created.GetProperty("id").GetInt32();

        var response = await client.PatchAsJsonAsync($"/api/tasks/{taskId}", new { assigned_to = 999999 }, Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // The task is untouched - it did not end up assigned to a phantom.
        var detail = await (await client.GetAsync($"/api/incidents/{incidentId}")).Content
            .ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("tasks")[0].GetProperty("assigned_to").ValueKind);
    }

    [Fact]
    public async Task CreateTask_WithAValidAssignee_RecordsWhoGotIt()
    {
        using var assignerClient = await AuthenticatedClientAsync("assign.valid@example.com", "Valid Assigner");
        using var assigneeClient = await AuthenticatedClientAsync("assign.worker@example.com", "Worker Bee");

        var incidentId = await CreateIncidentAsync(assignerClient);

        var me = await (await assigneeClient.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<JsonElement>(Json);
        var workerId = me.GetProperty("id").GetInt32();

        var response = await assignerClient.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/tasks", new { title = "Check the logs", assigned_to = workerId }, Json
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(workerId, body.GetProperty("assigned_to").GetInt32());

        // Creating an already-assigned task now says so in the feed, rather
        // than recording "added task" and leaving the assignment invisible.
        var detail = await (await assignerClient.GetAsync($"/api/incidents/{incidentId}")).Content
            .ReadFromJsonAsync<JsonElement>(Json);
        var logs = detail.GetProperty("activity_logs").EnumerateArray();
        Assert.Contains(logs, log => log.GetProperty("message").GetString()!.Contains("Worker Bee"));
    }
}
