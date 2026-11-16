// Who is allowed to see which incidents.
//
// The rule, in one sentence: you see an incident if you are involved in it,
// and leaders see everything.
//
// "Involved" deliberately covers three ways of being on an incident, because
// any one of them on its own would hide work from someone who is plainly
// part of it:
//   1. You created it.
//   2. You joined it (you are a participant).
//   3. Someone assigned you a task on it.
//
// Case 3 is the one that matters most in practice. Assigning a task is how
// work actually reaches people here - if that did not grant visibility,
// you could be given a job on an incident whose page you were not allowed
// to open, which is obviously useless.
using System.Linq.Expressions;
using IncidentFlow.Api.Entities;

namespace IncidentFlow.Api.Common;

public static class IncidentAccess
{
    // Everyone at this level and above sees every incident, involved or not.
    //
    // MANAGER is the cut-off because that is where this company's ladder
    // stops being individual contributors and starts being management - see
    // the comments on the Seniority enum in Entities/Enums.cs, which calls
    // MANAGER and above "senior management". A LEAD is deliberately NOT a
    // leader for this purpose: a tech lead is a hands-on engineer who owns a
    // system, not someone with oversight of the whole company's incidents.
    //
    // This is one named constant precisely so the bar can be moved in one
    // place (e.g. to DIRECTOR) without hunting through endpoint code.
    public const Seniority LeaderLevel = Seniority.MANAGER;

    // Who an incident gets escalated TO. Deliberately a rung lower than
    // LeaderLevel above, and the difference is the point: escalating is
    // asking for someone who can actually fix the thing, and in this company
    // that starts at the tech lead who owns the system. Visibility is a
    // different question - that one is about oversight, which starts at
    // MANAGER. Two rules, two constants, so moving one never quietly moves
    // the other. See Endpoints/IncidentEndpoints.cs for the escalate route.
    public const Seniority EscalationLevel = Seniority.LEAD;

    // Seniority is a C# enum whose members are declared in rank order, so
    // ">=" means "at this level or above" - see Entities/Enums.cs. Note this
    // comparison only works in memory: the column itself stores the text
    // name ("DIRECTOR"), so comparing it inside a database query would
    // compare strings alphabetically and give nonsense. That is fine here
    // because the current user is always already loaded as an object.
    public static bool IsLeader(User user) => user.Seniority >= LeaderLevel;

    // The "am I involved?" rule as an Expression, which is what lets Entity
    // Framework translate it into SQL and filter in the database rather than
    // fetching every incident and discarding most of them in C#.
    //
    // Leaders are NOT handled here on purpose: this returns a filter over
    // incidents, and a leader's answer is "don't filter at all". See
    // VisibleToAsync below for the version that applies both halves.
    public static Expression<Func<Incident, bool>> InvolvedIn(int userId) =>
        incident =>
            incident.CreatedBy == userId
            || incident.Participants.Any(p => p.UserId == userId)
            || incident.Tasks.Any(t => t.AssignedTo == userId);

    // The same rule again, for an incident that is already loaded in memory
    // (the detail endpoint has the full object, with its tasks and
    // participants, so it has no reason to go back to the database).
    //
    // Yes, the condition is written twice - once as an Expression above for
    // SQL and once as plain C# here. That duplication is a normal cost of
    // filtering in the database, but it does mean the two MUST be changed
    // together: if you add a fourth way of being involved, add it in both.
    public static bool CanSee(Incident incident, User user) =>
        IsLeader(user)
        || incident.CreatedBy == user.Id
        || incident.Participants.Any(p => p.UserId == user.Id)
        || incident.Tasks.Any(t => t.AssignedTo == user.Id);

    // Applies the visibility rule to a query: leaders get everything,
    // everyone else gets only what they are involved in.
    public static IQueryable<Incident> VisibleTo(this IQueryable<Incident> incidents, User user) =>
        IsLeader(user) ? incidents : incidents.Where(InvolvedIn(user.Id));

    // Sorts incidents the way the list page wants to read them: the ones
    // still needing attention first.
    //
    // Why a hand-written 0/1/2 rank instead of just OrderBy(i => i.Status):
    // the status column stores the enum's TEXT name (see AppDbContext), so
    // sorting by it sorts alphabetically - INVESTIGATING, OPEN, RESOLVED -
    // which is not the order anyone wants. The conditional below becomes a
    // CASE WHEN in SQL and gives the real lifecycle order instead.
    // Within a status, newest first, so a fresh outage is at the top.
    public static IOrderedQueryable<Incident> InWorkOrder(this IQueryable<Incident> incidents) =>
        incidents
            .OrderBy(i => i.Status == IncidentStatus.OPEN ? 0
                        : i.Status == IncidentStatus.INVESTIGATING ? 1
                        : 2)
            .ThenByDescending(i => i.CreatedAt);
}
