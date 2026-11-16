// How quickly somebody has to *acknowledge* an incident, by severity.
//
// "Acknowledge" means a human has seen it and taken it on - not that it is
// fixed. That distinction is the whole point of measuring it separately:
// an outage nobody has even looked at for twenty minutes is a different kind
// of problem from one three people are actively working on.
//
// The two numbers this file produces are the ones real incident tools
// report on:
//   MTTA - mean time to acknowledge (AcknowledgedAt - CreatedAt)
//   MTTR - mean time to resolve     (ResolvedAt     - CreatedAt)
using IncidentFlow.Api.Entities;

namespace IncidentFlow.Api.Common;

public static class SlaPolicy
{
    // The budget for each severity, in minutes. These live in one dictionary
    // rather than scattered through the endpoints so that "what is our
    // CRITICAL SLA?" has exactly one answer, and changing it is a one-line
    // edit here.
    //
    // Using a Dictionary keyed by the enum (rather than a switch) means the
    // compiler cannot catch a missing entry, so MinutesFor below falls back
    // to the LOW budget instead of throwing if a new severity is ever added.
    private static readonly Dictionary<Severity, int> BudgetMinutes = new()
    {
        [Severity.CRITICAL] = 15,
        [Severity.HIGH] = 60,
        [Severity.MEDIUM] = 240,    // 4 hours
        [Severity.LOW] = 1440,      // 24 hours
    };

    public static int MinutesFor(Severity severity) =>
        BudgetMinutes.TryGetValue(severity, out var minutes) ? minutes : BudgetMinutes[Severity.LOW];

    // The moment the clock runs out for this incident. Measured from when it
    // was created, because that is when the problem started mattering - not
    // from when someone got round to opening the page.
    public static DateTime AcknowledgeDeadline(Incident incident) =>
        incident.CreatedAt.AddMinutes(MinutesFor(incident.Severity));

    // Has this incident blown its acknowledgement budget?
    //
    // Three conditions, all of which have to hold:
    //   - nobody has acknowledged it,
    //   - it is not already resolved (an incident someone quietly fixed
    //     without clicking "acknowledge" first is not an SLA failure worth
    //     shouting about),
    //   - the deadline is in the past.
    //
    // `now` is passed in rather than read from DateTime.UtcNow inside here,
    // so a test can ask "would this be breached in three hours?" without
    // waiting three hours. That one parameter is the difference between a
    // rule you can test and one you can only hope about.
    public static bool IsBreached(Incident incident, DateTime now) =>
        incident.AcknowledgedAt is null
        && incident.Status != IncidentStatus.RESOLVED
        && now > AcknowledgeDeadline(incident);
}
