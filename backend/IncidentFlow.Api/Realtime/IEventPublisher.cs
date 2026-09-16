namespace IncidentFlow.Api.Realtime;

// Call this from an endpoint whenever something changes (status update, new
// task, etc). Implementations decide how the event actually reaches
// connected browser tabs - in production that's via Redis (see
// RedisEventPublisher); tests swap in LocalEventPublisher through the DI
// container so they don't need a real Redis server.
public interface IEventPublisher
{
    Task PublishEventAsync(int incidentId, string eventType, object data);
}
