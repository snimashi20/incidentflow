using StackExchange.Redis;

namespace IncidentFlow.Api.Realtime;

// A BackgroundService is ASP.NET Core's way of running a long-lived task
// alongside normal request handling for the whole lifetime of the server -
// it starts once when the app boots and is cancelled once when the app shuts
// down.
//
// It subscribes to EVERY incident's channel at once using a wildcard pattern
// ("incident:*"), then for each message figures out which incident it
// belongs to and hands it to IncidentSocketManager, which pushes it out to
// the right browser tabs. This is the piece that makes Redis Pub/Sub a real,
// working part of the app rather than just a buzzword.
public class RedisListenerBackgroundService(
    IConnectionMultiplexer redis,
    IncidentSocketManager sockets,
    ILogger<RedisListenerBackgroundService> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var subscriber = redis.GetSubscriber();
        var pattern = RedisChannel.Pattern($"{RedisChannels.IncidentPrefix}*");

        // StackExchange.Redis calls this handler on its own background thread
        // every time a matching message arrives - no manual receive loop needed.
        await subscriber.SubscribeAsync(pattern, async (channel, message) =>
        {
            try
            {
                var channelName = channel.ToString();
                var idText = channelName[RedisChannels.IncidentPrefix.Length..];
                if (!int.TryParse(idText, out var incidentId)) return;

                await sockets.BroadcastRawLocalAsync(incidentId, message.ToString());
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process pub/sub message on channel {Channel}", channel);
            }
        });

        logger.LogInformation("Subscribed to Redis pattern {Pattern}", pattern);

        // Keep this background service alive until the app shuts down; the
        // actual work above happens in the subscription callback, not here.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
        finally
        {
            await subscriber.UnsubscribeAsync(pattern);
        }
    }
}
