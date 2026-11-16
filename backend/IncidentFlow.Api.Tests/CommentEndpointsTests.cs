// Tests for the incident conversation - posting comments and deleting your
// own. See Endpoints/CommentEndpoints.cs.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class CommentEndpointsTests : IntegrationTestBase
{
    private async Task<int> CreateIncidentAsync(HttpClient client, string title = "Comment me")
    {
        var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new { title, description = "", severity = "LOW", department_id = await FirstDepartmentIdAsync() },
            Json
        );
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
    }

    [Fact]
    public async Task PostComment_ShowsUpOnTheIncident()
    {
        using var client = await AuthenticatedClientAsync("commenter@example.com", "Commenter");
        var incidentId = await CreateIncidentAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/comments", new { body = "Rolling back the deploy now" }, Json
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("Rolling back the deploy now", created.GetProperty("body").GetString());
        Assert.Equal("Commenter", created.GetProperty("user").GetProperty("name").GetString());

        // And it comes back as part of the incident, which is how the detail
        // page gets the thread on first load.
        var detail = await (await client.GetAsync($"/api/incidents/{incidentId}")).Content
            .ReadFromJsonAsync<JsonElement>(Json);
        var comments = detail.GetProperty("comments");
        Assert.Equal(1, comments.GetArrayLength());
        Assert.Equal("Rolling back the deploy now", comments[0].GetProperty("body").GetString());
    }

    [Fact]
    public async Task PostComment_RejectsEmptyText()
    {
        using var client = await AuthenticatedClientAsync("empty@example.com", "Empty");
        var incidentId = await CreateIncidentAsync(client);

        // Whitespace only - trimmed to nothing, so it is just as empty.
        var response = await client.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/comments", new { body = "   " }, Json
        );

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task PostComment_IsForbiddenForSomeoneNotInvolved()
    {
        using var ownerClient = await AuthenticatedClientAsync("thread.owner@example.com", "Thread Owner");
        using var strangerClient = await AuthenticatedClientAsync("thread.stranger@example.com", "Thread Stranger");

        var incidentId = await CreateIncidentAsync(ownerClient, "Not your conversation");

        // Being unable to read the incident has to mean being unable to talk
        // on it, or the visibility rule leaks through the comment box.
        var response = await strangerClient.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/comments", new { body = "butting in" }, Json
        );

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteComment_OnlyTheAuthorIsAllowed()
    {
        using var authorClient = await AuthenticatedClientAsync("author@example.com", "Author");
        using var otherClient = await AuthenticatedClientAsync("heckler@example.com", "Heckler");

        var incidentId = await CreateIncidentAsync(authorClient, "Shared incident");

        // The second person joins, so they CAN see the incident and read the
        // thread - this test is specifically about the stricter rule that
        // reading someone's message does not let you delete it.
        await otherClient.PostAsync($"/api/incidents/{incidentId}/join", null);

        var created = await (await authorClient.PostAsJsonAsync(
            $"/api/incidents/{incidentId}/comments", new { body = "my message" }, Json
        )).Content.ReadFromJsonAsync<JsonElement>(Json);
        var commentId = created.GetProperty("id").GetInt32();

        var refused = await otherClient.DeleteAsync($"/api/comments/{commentId}");
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        // The author can, and the comment really goes away.
        var allowed = await authorClient.DeleteAsync($"/api/comments/{commentId}");
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);

        var detail = await (await authorClient.GetAsync($"/api/incidents/{incidentId}")).Content
            .ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal(0, detail.GetProperty("comments").GetArrayLength());
    }
}
