using System.Text.Json;
using IncidentFlow.Api.Common;
using StackExchange.Redis;

namespace IncidentFlow.Api.Realtime;

// Publishes an event to the incident's Redis channel. Every backend replica
// is subscribed (see RedisListenerBackgroundService), so this is what lets a
// change made on server A reach a browser connected to server B.
public class RedisEventPublisher(
    IConnectionMultiplexer redis,
    ILogger<RedisEventPublisher> logger
) : IEventPublisher
{
    public async Task PublishEventAsync(int incidentId, string eventType, object data)
    {
        var message = JsonSerializer.Serialize(new { type = eventType, data }, JsonConfig.Options);

        // A failed publish is logged and swallowed, never thrown.
        //
        // This matters because of WHEN publishing happens: every caller
        // commits its database transaction first and publishes afterwards.
        // By the time we get here the write has already succeeded and is
        // permanent. Letting a Redis timeout bubble up from here would hand
        // the user a 500 for an operation that worked perfectly - they would
        // reasonably retry it, and create a second incident.
        //
        // The cost of swallowing it is a live update that some browsers miss,
        // which is a far smaller problem, and one the client already recovers
        // from: the frontend re-fetches the incident whenever its socket
        // reconnects (see useIncidentSocket.ts).
        //
        // The proper fix for not losing events at all is a transactional
        // outbox - write the event into the same database transaction as the
        // change, and have a separate worker deliver it - which is a bigger
        // change than this project needs today.
        try
        {
            await redis.GetSubscriber().PublishAsync(
                RedisChannel.Literal(RedisChannels.ForIncident(incidentId)), message
            );
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Could not publish {EventType} for incident {IncidentId}. The database change was saved; "
                    + "connected clients will not see it until they reconnect.",
                eventType, incidentId
            );
        }
    }
}
