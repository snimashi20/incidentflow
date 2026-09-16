using System.Text.Json;
using IncidentFlow.Api.Common;
using StackExchange.Redis;

namespace IncidentFlow.Api.Realtime;

// The production implementation of IEventPublisher: doesn't touch any
// WebSocket directly, it just drops the event onto this incident's Redis
// channel. RedisListenerBackgroundService is what actually picks it back up
// and forwards it to connected browsers.
//
// Why go through Redis instead of just calling IncidentSocketManager
// directly? Because if you ever run more than one backend replica (e.g.
// behind a load balancer), each replica only knows about the WebSocket
// connections attached to IT. Redis Pub/Sub is a shared message bus every
// replica listens to, so an event published by replica A still reaches a
// browser connected to replica B.
public class RedisEventPublisher(IConnectionMultiplexer redis) : IEventPublisher
{
    public Task PublishEventAsync(int incidentId, string eventType, object data)
    {
        var message = JsonSerializer.Serialize(new { type = eventType, data }, JsonConfig.Options);
        return redis.GetSubscriber().PublishAsync(
            RedisChannel.Literal(RedisChannels.ForIncident(incidentId)), message
        );
    }
}
