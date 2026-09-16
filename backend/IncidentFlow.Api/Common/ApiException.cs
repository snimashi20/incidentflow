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

    public static ApiException UnprocessableEntity(string detail) =>
        new(StatusCodes.Status422UnprocessableEntity, detail);
}
