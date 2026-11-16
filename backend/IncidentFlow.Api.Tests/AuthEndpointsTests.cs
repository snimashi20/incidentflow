// Tests for register/login/me. Each test method is independent -
// IntegrationTestBase gives every test a fresh empty database, so tests
// can't affect each other.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class AuthEndpointsTests : IntegrationTestBase
{
    [Fact]
    public async Task Register_CreatesUser()
    {
        var departmentId = await FirstDepartmentIdAsync();

        var response = await Client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email = "alice@example.com",
                name = "Alice",
                password = "supersecret123",
                department_id = departmentId,
                job_title = "Senior Site Reliability Engineer",
                seniority = "SENIOR",
            },
            Json
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("alice@example.com", body.GetProperty("email").GetString());
        // Confirms the response never leaks the password hash back to the client.
        Assert.False(body.TryGetProperty("hashed_password", out _));
        // ...and that the new account really is attached to a department.
        Assert.Equal(departmentId, body.GetProperty("department").GetProperty("id").GetInt32());
        Assert.Equal("Senior Site Reliability Engineer", body.GetProperty("job_title").GetString());
        Assert.Equal("SENIOR", body.GetProperty("seniority").GetString());
    }

    [Fact]
    public async Task Register_DefaultsToMidLevel_WhenNoneGiven()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email = "unspecified@example.com",
                name = "Unspecified Level",
                password = "supersecret123",
                department_id = await FirstDepartmentIdAsync(),
            },
            Json
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("MID", body.GetProperty("seniority").GetString());
    }

    [Fact]
    public async Task Register_WithUnknownSeniority_Rejected()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email = "overpromoted@example.com",
                name = "Over Promoted",
                password = "supersecret123",
                department_id = await FirstDepartmentIdAsync(),
                seniority = "SUPREME_LEADER",
            },
            Json
        );

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithoutValidDepartment_Rejected()
    {
        // 999999 is an id no department could have - every employee must
        // belong to a real department, so this must not create an account.
        var response = await Client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email = "nobody@example.com",
                name = "Nobody",
                password = "supersecret123",
                department_id = 999999,
            },
            Json
        );

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Rejected()
    {
        var payload = new
        {
            email = "bob@example.com",
            name = "Bob",
            password = "supersecret123",
            department_id = await FirstDepartmentIdAsync(),
        };
        await Client.PostAsJsonAsync("/api/auth/register", payload, Json);

        // Registering the same email twice should fail the second time.
        var response = await Client.PostAsJsonAsync("/api/auth/register", payload, Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_Success_ReturnsToken()
    {
        await Client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email = "carol@example.com",
                name = "Carol",
                password = "supersecret123",
                department_id = await FirstDepartmentIdAsync(),
            },
            Json
        );

        var response = await Client.PostAsJsonAsync(
            "/api/auth/login", new { email = "carol@example.com", password = "supersecret123" }, Json
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("bearer", body.GetProperty("token_type").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("access_token").GetString()));
    }

    [Fact]
    public async Task Login_WrongPassword_Rejected()
    {
        await Client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                email = "dave@example.com",
                name = "Dave",
                password = "supersecret123",
                department_id = await FirstDepartmentIdAsync(),
            },
            Json
        );

        var response = await Client.PostAsJsonAsync(
            "/api/auth/login", new { email = "dave@example.com", password = "wrongpassword" }, Json
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_RequiresAuthentication()
    {
        // No Authorization header sent at all -> should be rejected.
        var response = await Client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsCurrentUser()
    {
        var token = await RegisterAndLoginAsync("erin@example.com", "Erin");
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await Client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Equal("erin@example.com", body.GetProperty("email").GetString());
    }
}
