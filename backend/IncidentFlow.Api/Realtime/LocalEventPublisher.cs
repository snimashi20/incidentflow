namespace IncidentFlow.Api.Realtime;

// A test-only stand-in for RedisEventPublisher: skips the real Redis hop and
// broadcasts straight to local sockets. Used by the test project (see
// IncidentFlow.Api.Tests/IntegrationTestBase.cs) so tests can exercise the
// WebSocket broadcast behavior without needing a live Redis server.
public class LocalEventPublisher(IncidentSocketManager sockets) : IEventPublisher
{
    public Task PublishEventAsync(int incidentId, string eventType, object data) =>
        sockets.BroadcastLocalAsync(incidentId, new { type = eventType, data });
}
