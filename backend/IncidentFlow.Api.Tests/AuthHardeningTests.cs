// Tests for the two things standing between /api/auth and a password
// guesser: a length floor on new passwords, and a rate limit on login.
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IncidentFlow.Api.Common;
using IncidentFlow.Api.Security;
using Xunit;

namespace IncidentFlow.Api.Tests;

public class AuthHardeningTests : IntegrationTestBase
{
    private async Task<HttpResponseMessage> RegisterWithPasswordAsync(string email, string password) =>
        await Client.PostAsJsonAsync(
            "/api/auth/register",
            new { email, name = "Test Person", password, department_id = await FirstDepartmentIdAsync() },
            Json
        );

    // ---- Password policy ---------------------------------------------------

    [Fact]
    public async Task Register_RejectsAnEmptyPassword()
    {
        // This is the one that used to get through. An account with an empty
        // password is not a weak account, it is an open door.
        var response = await RegisterWithPasswordAsync("empty.password@example.com", "");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Register_RejectsAWhitespacePassword()
    {
        var response = await RegisterWithPasswordAsync("spaces@example.com", "        ");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Register_RejectsAShortPassword_AndSaysWhy()
    {
        var response = await RegisterWithPasswordAsync("shorty@example.com", "pass1");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        // The message has to name the actual requirement - "invalid password"
        // leaves the person guessing at what would be acceptable.
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Contains(PasswordPolicy.MinimumLength.ToString(), body.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Register_RejectsAPasswordLongerThanBCryptCanHash()
    {
        // BCrypt ignores everything past 72 bytes. Accepting a 200-character
        // passphrase would mean silently authenticating people on the first
        // 72 of it - so it is refused rather than quietly truncated.
        var response = await RegisterWithPasswordAsync(
            "novelist@example.com", new string('x', PasswordPolicy.MaximumLength + 1)
        );

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Register_AcceptsAPasswordAtExactlyTheMinimumLength()
    {
        // Guards the boundary from the other side: an off-by-one in the rule
        // would lock out passwords that are supposed to be fine.
        var response = await RegisterWithPasswordAsync(
            "just.long.enough@example.com", new string('a', PasswordPolicy.MinimumLength)
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---- Login rate limiting ----------------------------------------------

    [Fact]
    public async Task Login_IsRateLimitedPerIpAddress()
    {
        await RegisterWithPasswordAsync("target@example.com", "supersecret123");

        var wrongPassword = new { email = "target@example.com", password = "guess" };
        var statuses = new List<HttpStatusCode>();

        // Attack the account a few more times than the policy allows. Each
        // test method gets its own application instance, so this test's
        // attempts cannot leak into any other test's budget.
        for (var attempt = 0; attempt < RateLimitPolicies.LoginPermitsPerWindow + 3; attempt++)
        {
            var response = await Client.PostAsJsonAsync("/api/auth/login", wrongPassword, Json);
            statuses.Add(response.StatusCode);
        }

        // The early attempts are refused on the merits (401)...
        Assert.Equal(HttpStatusCode.Unauthorized, statuses[0]);

        // ...and once the budget is gone, the endpoint stops answering
        // password questions at all. That is the difference between an
        // account somebody can grind at and one they cannot.
        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);

        // Exactly the allowed number got through to the password check.
        Assert.Equal(
            RateLimitPolicies.LoginPermitsPerWindow,
            statuses.Count(status => status == HttpStatusCode.Unauthorized)
        );
    }

    [Fact]
    public async Task Login_RateLimitRejection_UsesTheSameErrorShapeAsEverythingElse()
    {
        var wrongPassword = new { email = "nobody@example.com", password = "guess" };

        HttpResponseMessage? throttled = null;
        for (var attempt = 0; attempt < RateLimitPolicies.LoginPermitsPerWindow + 3; attempt++)
        {
            var response = await Client.PostAsJsonAsync("/api/auth/login", wrongPassword, Json);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throttled = response;
                break;
            }
        }

        Assert.NotNull(throttled);

        // A client that already knows how to read {"detail": "..."} from
        // every other error should not need a second code path for this one.
        var body = await throttled!.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task Me_IsNotRateLimited()
    {
        // /api/auth/me is called on every page load, so throttling it would
        // break normal use while stopping no attack. This test is what keeps
        // somebody from "tidying up" by moving the limit onto the whole group.
        var token = await RegisterAndLoginAsync("busy.user@example.com", "Busy User");
        using var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        for (var request = 0; request < RateLimitPolicies.LoginPermitsPerWindow + 5; request++)
        {
            var response = await client.GetAsync("/api/auth/me");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
