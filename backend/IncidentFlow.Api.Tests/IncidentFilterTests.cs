// Tests for the filters and search on GET /api/incidents.
//
// Every test here registers its user at DIRECTOR, which is above
// IncidentAccess.LeaderLevel, so they see every incident. That keeps each
// test about the FILTER being tested rather than about visibility - which
// has its own tests over in IncidentEndpointsTests.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class IncidentFilterTests : IntegrationTestBase
{
    private async Task<int> CreateIncidentAsync(
        HttpClient client, string title, string severity = "LOW", string description = ""
    )
    {
        var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new { title, description, severity, department_id = await FirstDepartmentIdAsync() },
            Json
        );
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
    }

    private static async Task<List<string>> TitlesAsync(HttpClient client, string query)
    {
        var body = await (await client.GetAsync($"/api/incidents{query}")).Content
            .ReadFromJsonAsync<JsonElement>(Json);
        return [.. body.EnumerateArray().Select(i => i.GetProperty("title").GetString()!)];
    }

    [Fact]
    public async Task FilterByStatus()
    {
        using var client = await AuthenticatedClientAsync("filter.status@example.com", "Filter", seniority: "DIRECTOR");

        var openId = await CreateIncidentAsync(client, "Still open");
        var resolvedId = await CreateIncidentAsync(client, "All done");
        await client.PatchAsJsonAsync($"/api/incidents/{resolvedId}/status", new { status = "RESOLVED" }, Json);

        var openTitles = await TitlesAsync(client, "?status=OPEN");
        Assert.Contains("Still open", openTitles);
        Assert.DoesNotContain("All done", openTitles);

        var resolvedTitles = await TitlesAsync(client, "?status=RESOLVED");
        Assert.Contains("All done", resolvedTitles);
        Assert.DoesNotContain("Still open", resolvedTitles);

        Assert.True(openId > 0); // created successfully
    }

    [Fact]
    public async Task FilterBySeverity()
    {
        using var client = await AuthenticatedClientAsync("filter.sev@example.com", "Filter Sev", seniority: "DIRECTOR");

        await CreateIncidentAsync(client, "Everything is on fire", "CRITICAL");
        await CreateIncidentAsync(client, "Minor typo", "LOW");

        var titles = await TitlesAsync(client, "?severity=CRITICAL");

        Assert.Contains("Everything is on fire", titles);
        Assert.DoesNotContain("Minor typo", titles);
    }

    [Fact]
    public async Task SearchMatchesTitleAndDescription_IgnoringCase()
    {
        using var client = await AuthenticatedClientAsync("searcher@example.com", "Searcher", seniority: "DIRECTOR");

        await CreateIncidentAsync(client, "Checkout API returning 500s");
        await CreateIncidentAsync(client, "Slow dashboard", description: "The CHECKOUT page takes 9 seconds");
        await CreateIncidentAsync(client, "Unrelated incident");

        // Lower-case search term against a mixed-case title and an
        // upper-case description - both should match.
        var titles = await TitlesAsync(client, "?q=checkout");

        Assert.Contains("Checkout API returning 500s", titles);
        Assert.Contains("Slow dashboard", titles);
        Assert.DoesNotContain("Unrelated incident", titles);
    }

    [Fact]
    public async Task MineFilter_NarrowsALeadersViewToTheirOwnWork()
    {
        using var leaderClient = await AuthenticatedClientAsync("busy.director@example.com", "Busy Director", seniority: "DIRECTOR");
        using var otherClient = await AuthenticatedClientAsync("someone.else@example.com", "Someone Else");

        await CreateIncidentAsync(leaderClient, "My own incident");
        await CreateIncidentAsync(otherClient, "Somebody elses incident");

        // Without the filter, a leader sees both...
        var everything = await TitlesAsync(leaderClient, "");
        Assert.Contains("My own incident", everything);
        Assert.Contains("Somebody elses incident", everything);

        // ...and with it, only the one they are actually on.
        var mine = await TitlesAsync(leaderClient, "?mine=true");
        Assert.Contains("My own incident", mine);
        Assert.DoesNotContain("Somebody elses incident", mine);
    }

    [Fact]
    public async Task Pagination_LimitsHowManyComeBack()
    {
        using var client = await AuthenticatedClientAsync("pager@example.com", "Pager", seniority: "DIRECTOR");

        await CreateIncidentAsync(client, "Page one incident");
        await CreateIncidentAsync(client, "Page two incident");
        await CreateIncidentAsync(client, "Page three incident");

        var firstPage = await TitlesAsync(client, "?page=1&page_size=2");
        Assert.Equal(2, firstPage.Count);

        var secondPage = await TitlesAsync(client, "?page=2&page_size=2");
        Assert.Single(secondPage);

        // No incident appears on both pages.
        Assert.Empty(firstPage.Intersect(secondPage));
    }

    [Fact]
    public async Task CombinedFilters_AreAnded()
    {
        using var client = await AuthenticatedClientAsync("combo@example.com", "Combo", seniority: "DIRECTOR");

        await CreateIncidentAsync(client, "Redis cluster down", "CRITICAL");
        await CreateIncidentAsync(client, "Redis slow", "LOW");
        await CreateIncidentAsync(client, "Postgres cluster down", "CRITICAL");

        var titles = await TitlesAsync(client, "?q=redis&severity=CRITICAL");

        Assert.Equal(["Redis cluster down"], titles);
    }

    [Fact]
    public async Task UnknownStatus_IsRejectedWithAHelpfulMessage()
    {
        using var client = await AuthenticatedClientAsync("typo@example.com", "Typo", seniority: "DIRECTOR");

        var response = await client.GetAsync("/api/incidents?status=INVESTIGATNG");

        // 422 with a readable message, rather than ASP.NET's bodyless 400
        // from a failed enum bind - a typo in a query string should tell you
        // what the valid values are.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains("INVESTIGATING", body.GetProperty("detail").GetString());
    }
}
