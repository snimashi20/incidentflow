using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace IncidentFlow.Api.Security;

// A JWT (JSON Web Token) is a signed, self-contained "login ticket" the
// client stores and sends back on every request instead of a password.
// Because it's signed with our secret key, the server can trust it later
// without needing to look anything up in a session store - that's what makes
// JWT auth "stateless".
public class JwtService(AppOptions options)
{
    private readonly SymmetricSecurityKey _signingKey =
        new(Encoding.UTF8.GetBytes(options.JwtSecret));

    // Builds a signed JWT containing the user's email ("sub" = subject) and
    // an expiry time.
    public string CreateAccessToken(string subjectEmail)
    {
        var credentials = new SigningCredentials(_signingKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            claims: [new Claim(JwtRegisteredClaimNames.Sub, subjectEmail)],
            expires: DateTime.UtcNow.AddMinutes(options.AccessTokenExpireMinutes),
            signingCredentials: credentials
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Verifies a token's signature and expiry, and returns the email it was
    // issued for - or null if the token is missing, expired, or tampered with.
    public string? ValidateAndGetEmail(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;

        // By default JwtSecurityTokenHandler remaps short claim names like
        // "sub" to legacy long-form claim type URIs when reading a validated
        // token back out, so FindFirstValue(JwtRegisteredClaimNames.Sub)
        // would silently return null even for a perfectly valid token.
        // Disabling that keeps the claim type exactly as we wrote it.
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            // A little leeway avoids spurious failures if the server's clock
            // and the token's expiry are off by a second or two.
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        try
        {
            var principal = handler.ValidateToken(token, validationParameters, out _);
            return principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        }
        catch (SecurityTokenException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            // Thrown for a malformed token string (not even shaped like a JWT).
            return null;
        }
    }
}
