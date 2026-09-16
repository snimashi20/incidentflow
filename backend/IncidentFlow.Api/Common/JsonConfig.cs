using System.Text.Json;
using System.Text.Json.Serialization;

namespace IncidentFlow.Api.Common;

// The frontend (frontend/src/types/index.ts) expects snake_case field names
// (e.g. "incident_id", "created_at"). C#'s own convention is PascalCase
// ("IncidentId"), so without this, every JSON response would silently stop
// matching what the frontend expects. This one options object is shared by
// both the HTTP API (Program.cs) and the WebSocket broadcasts (Realtime/),
// so every JSON payload leaving this backend uses the same, frontend-compatible shape:
//   - property names in snake_case
//   - enum values as their text name ("HIGH"), not a number
public static class JsonConfig
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        // No naming policy here (unlike the property names above): our enum
        // members are already spelled exactly as the API/frontend contract
        // expects (e.g. Severity.HIGH -> "HIGH", IncidentTaskStatus.IN_PROGRESS
        // -> "IN_PROGRESS"), so we want the raw member name, untransformed.
        Converters = { new JsonStringEnumConverter() },
    };
}
