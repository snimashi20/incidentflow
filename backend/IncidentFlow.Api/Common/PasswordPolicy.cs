// What counts as an acceptable password when registering.
//
// Deliberately short and boring. The rules people usually reach for first -
// "must contain a digit, a symbol and a capital letter" - reliably produce
// `Password1!` and little else, while a length floor is the one requirement
// that actually buys resistance to guessing. So: a minimum length, a maximum
// length (because BCrypt silently ignores everything past 72 bytes, and a
// password that is quietly truncated is worse than one that is refused), and
// nothing else.
namespace IncidentFlow.Api.Common;

public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    // BCrypt's own hard limit is 72 bytes. Anything longer is not "extra
    // secure", it is ignored - so refuse it plainly rather than accepting a
    // 200-character passphrase and only using the first 72.
    public const int MaximumLength = 72;

    // Returns null when the password is fine, or the reason it isn't. A
    // string-or-null return keeps the call site to one `if`, and puts the
    // wording next to the rule it describes instead of in the endpoint.
    public static string? Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "Please choose a password";
        }

        if (password.Length < MinimumLength)
        {
            return $"Password must be at least {MinimumLength} characters";
        }

        if (password.Length > MaximumLength)
        {
            return $"Password must be at most {MaximumLength} characters";
        }

        return null;
    }
}
