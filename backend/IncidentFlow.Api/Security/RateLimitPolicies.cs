// Names for the rate limiting policies, so the endpoint that opts in and the
// Program.cs code that defines the policy cannot drift apart over a typo -
// `.RequireRateLimiting("logins")` against a policy registered as "login"
// throws at startup, which is better than a silent miss, but a shared
// constant means it cannot happen at all.
namespace IncidentFlow.Api.Security;

public static class RateLimitPolicies
{
    public const string Login = "login";

    // How many login attempts one IP address gets per window.
    //
    // Ten a minute is far more than a person needs - even fumbling a
    // password three times and a typo'd email leaves room - while a script
    // working through a password list hits the wall on its eleventh try.
    //
    // Worth being honest about the limits of this: partitioning by IP is the
    // right default, but it means a whole office behind one NAT shares a
    // budget, and an attacker with a thousand addresses gets a thousand
    // budgets. Real protection pairs this with per-ACCOUNT throttling and
    // lockout, which this project does not have yet.
    public const int LoginPermitsPerWindow = 10;

    public static readonly TimeSpan LoginWindow = TimeSpan.FromMinutes(1);
}
