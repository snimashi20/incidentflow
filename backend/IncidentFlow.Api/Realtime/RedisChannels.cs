namespace IncidentFlow.Api.Realtime;

// Every incident gets its own Redis Pub/Sub channel, named "incident:<id>",
// so updates to one incident are never sent to clients watching a different one.
public static class RedisChannels
{
    public const string IncidentPrefix = "incident:";

    public static string ForIncident(int incidentId) => $"{IncidentPrefix}{incidentId}";
}
