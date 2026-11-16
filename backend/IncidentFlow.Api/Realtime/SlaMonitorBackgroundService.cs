// Watches for incidents nobody has acknowledged in time.
//
// Everything else in this app happens because a person clicked something.
// This is the one piece that acts on its own: the whole point of an SLA is
// that NOT doing anything is the failure case, and nobody is going to click
// a button to report that they forgot.
//
// Split in two on purpose:
//   SlaMonitor                  - the rule, as one plain method you can call.
//   SlaMonitorBackgroundService - the timer that calls it every minute.
// That split is what makes this testable: a test can ask SlaMonitor "what
// would you do at 9am tomorrow?" and get an answer immediately, instead of
// starting a background service and waiting around for a real minute to pass.
using IncidentFlow.Api.Common;
using IncidentFlow.Api.Data;
using IncidentFlow.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Api.Realtime;

public static class SlaMonitor
{
    // Finds every incident that has blown its acknowledgement deadline,
    // stamps it, and tells anyone watching. Returns the incidents it marked,
    // so the caller (or a test) can see what happened.
    public static async Task<List<Incident>> RunOnceAsync(
        AppDbContext db, IEventPublisher events, DateTime now, CancellationToken cancellationToken = default
    )
    {
        // Narrow the search in the database as far as the schema allows:
        // only incidents nobody has acknowledged, that aren't already marked,
        // and that aren't resolved.
        //
        // The actual deadline comparison then happens in C#, because the
        // deadline depends on Severity, which is stored as TEXT - the same
        // reason seniority has to be ranked in memory (see
        // Common/IncidentAccess.cs). The rows left after these three filters
        // are the unattended ones, which in a healthy week is a short list.
        var candidates = await db.Incidents
            .Where(i => i.AcknowledgedAt == null
                && i.SlaBreachedAt == null
                && i.Status != IncidentStatus.RESOLVED)
            .ToListAsync(cancellationToken);

        var breached = candidates.Where(incident => SlaPolicy.IsBreached(incident, now)).ToList();

        if (breached.Count == 0) return breached;

        foreach (var incident in breached)
        {
            incident.SlaBreachedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var incident in breached)
        {
            // Deliberately NOT written to the activity feed. Every entry in
            // that feed names the person who did the thing, and no person did
            // this one - attributing it to the incident's creator would put
            // words in somebody's mouth. The breach lives on the incident
            // itself instead, where the UI shows it as a badge.
            await events.PublishEventAsync(incident.Id, "sla_breached", new
            {
                id = incident.Id,
                severity = incident.Severity.ToString(),
                sla_deadline = SlaPolicy.AcknowledgeDeadline(incident),
                sla_breached_at = incident.SlaBreachedAt,
            });
        }

        return breached;
    }
}

public class SlaMonitorBackgroundService(
    IServiceScopeFactory scopeFactory,
    IEventPublisher events,
    ILogger<SlaMonitorBackgroundService> logger
) : BackgroundService
{
    // Once a minute. Fine-grained enough that a 15-minute CRITICAL budget is
    // reported usefully, cheap enough that it costs nothing to leave running.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // PeriodicTimer is the modern way to write "every N seconds until
        // shutdown" - unlike Task.Delay in a loop, it doesn't drift and it
        // cancels cleanly.
        using var timer = new PeriodicTimer(Interval);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                // A BackgroundService is a singleton, but AppDbContext is
                // scoped (one per request, roughly one unit of work), so we
                // cannot just inject it. Creating a scope per tick is the
                // standard way around that - and it also means a failure on
                // one tick cannot poison the next one's database context.
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var breached = await SlaMonitor.RunOnceAsync(db, events, DateTime.UtcNow, stoppingToken);

                foreach (var incident in breached)
                {
                    logger.LogWarning(
                        "SLA breached: incident {IncidentId} ({Severity}) went unacknowledged past {Deadline:u}",
                        incident.Id, incident.Severity, SlaPolicy.AcknowledgeDeadline(incident)
                    );
                }
            }
            catch (Exception ex)
            {
                // Swallow and keep going. A background loop that dies on its
                // first bad tick (a dropped database connection, say) stops
                // monitoring forever, and nothing would tell you - which is
                // exactly the kind of silent failure this service exists to
                // catch in the first place.
                logger.LogError(ex, "SLA monitor tick failed");
            }
        }
    }

    // Returns false instead of throwing when the app is shutting down, so the
    // loop above ends quietly rather than logging a cancellation as an error.
    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
