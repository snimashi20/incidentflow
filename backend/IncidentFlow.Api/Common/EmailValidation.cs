using System.Text.RegularExpressions;

namespace IncidentFlow.Api.Common;

// Minimal APIs don't have a built-in "email" type, so this rejects obviously
// malformed addresses before an endpoint's own code ever runs. Not a full
// RFC 5322 validator, but enough to catch typos like "not-an-email".
public static partial class EmailValidation
{
    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$")]
    private static partial Regex Pattern();

    public static bool IsValid(string email) => !string.IsNullOrWhiteSpace(email) && Pattern().IsMatch(email);
}
