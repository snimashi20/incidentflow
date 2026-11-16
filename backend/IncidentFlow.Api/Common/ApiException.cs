namespace IncidentFlow.Api.Common;

// A small, uniform way to fail out of an endpoint: throw one of these from
// anywhere in an endpoint handler, and the middleware registered in
// Program.cs catches it and writes a `{"detail": "..."}` JSON response with
// the matching HTTP status code, so individual endpoints never need to build
// error responses by hand.
public class ApiException(int statusCode, string detail) : Exception(detail)
{
    public int StatusCode { get; } = statusCode;

    public string Detail { get; } = detail;

    // A couple of named constructors for the shapes we throw constantly,
    // so call sites read as `throw ApiException.NotFound("Incident not found")`.
    public static ApiException NotFound(string detail) => new(StatusCodes.Status404NotFound, detail);

    public static ApiException BadRequest(string detail) => new(StatusCodes.Status400BadRequest, detail);

    public static ApiException Unauthorized(string detail = "Could not validate credentials") =>
        new(StatusCodes.Status401Unauthorized, detail);

    // 401 vs 403 trips people up constantly, so: 401 Unauthorized means "I
    // don't know who you are" (no token, expired token) - logging in would
    // fix it. 403 Forbidden means "I know exactly who you are, and you're
    // still not allowed to do this" - logging in again changes nothing.
    // Deleting someone else's incident is the second kind.
    public static ApiException Forbidden(string detail) => new(StatusCodes.Status403Forbidden, detail);

    public static ApiException UnprocessableEntity(string detail) =>
        new(StatusCodes.Status422UnprocessableEntity, detail);

    // 409 Conflict: the request was fine, and would have been allowed, but
    // somebody else changed the same thing first. Unlike a 403, retrying
    // after a fresh read is exactly the right response - which is why it
    // needs to be its own status rather than a generic 500.
    public static ApiException Conflict(string detail) => new(StatusCodes.Status409Conflict, detail);
}
