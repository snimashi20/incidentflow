// Incident endpoints: create, list, view, change status, join, acknowledge,
// escalate, hand over command, and delete. Every mutating route here also
// does two extra things beyond the database write:
//   1. Records a human-readable line in the ActivityLog (the "Team Activity" feed).
//   2. Calls IEventPublisher.PublishEventAsync() so anyone else currently
//      viewing this incident sees the change immediately over their
//      WebSocket connection.
using IncidentFlow.Api.Common;
using IncidentFlow.Api.Data;
using IncidentFlow.Api.Dtos;
using IncidentFlow.Api.Entities;
using IncidentFlow.Api.Realtime;
using IncidentFlow.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Api.Endpoints;

public static class IncidentEndpoints
{
    // Fetches one incident together with everything the detail page shows, in
    // a small, fixed number of extra queries (EF Core calls this "eager
    // loading" via Include/ThenInclude) rather than one query per related row,
    // which would get slow as an incident grows more tasks/comments/logs.
    private static async Task<Incident> GetIncidentOr404Async(AppDbContext db, int incidentId)
    {
        var incident = await db.Incidents
            .Include(i => i.Tasks)
            .Include(i => i.ActivityLogs).ThenInclude(a => a.User)
            .Include(i => i.Participants).ThenInclude(p => p.User)
            .Include(i => i.Comments).ThenInclude(c => c.User)
            .Include(i => i.Department)
            .Include(i => i.Commander)
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == incidentId);

        return incident ?? throw ApiException.NotFound("Incident not found");
    }

    // Load an incident AND check the caller is allowed to see it. Every route
    // that works on a single incident starts with this, so a new endpoint
    // cannot accidentally be written without the access check - the only way
    // to get the incident is through the door that checks.
    private static async Task<Incident> RequireVisibleAsync(AppDbContext db, int incidentId, User user)
    {
        var incident = await GetIncidentOr404Async(db, incidentId);

        if (!IncidentAccess.CanSee(incident, user))
        {
            throw ApiException.Forbidden("You do not have access to this incident");
        }

        return incident;
    }

    // Small helper used by every route below to add one line to the activity
    // feed. We already have the User object in hand, so we just attach it to
    // the navigation property directly instead of reloading it from the database.
    //
    // Note what this does NOT do: save. It used to call SaveChangesAsync
    // itself, which meant a handler that changed an incident AND logged it
    // wrote to the database twice, independently - and a failure between the
    // two left the change recorded with no explanation of who made it, or an
    // activity line about a change that never happened. Now every handler
    // stages its changes and saves once, inside a transaction, so the change
    // and the record of it land together or not at all.
    //
    // The returned entity is safe to read AFTER the save: EF fills in the
    // database-generated Id, and SaveChanges stamps CreatedAt (see
    // AppDbContext.StampTimestamps).
    private static ActivityLog AddActivity(AppDbContext db, int incidentId, User user, string message)
    {
        var log = new ActivityLog { IncidentId = incidentId, UserId = user.Id, User = user, Message = message };
        db.ActivityLogs.Add(log);
        return log;
    }

    private static ActivityOut ToActivityOut(ActivityLog log) =>
        new(log.Id, log.Message, log.CreatedAt, new UserOut(log.User.Id, log.User.Email, log.User.Name));

    // Converts an EF Core Incident (database row + relationships) into the
    // IncidentDetail DTO we actually send over the API/WebSocket.
    private static IncidentDetail ToDetail(Incident incident) => new(
        incident.Id, incident.Title, incident.Description, incident.Severity, incident.Status,
        incident.CreatedBy, incident.CreatedAt, incident.UpdatedAt,
        incident.DepartmentId, incident.Department?.Name,
        incident.CommanderUserId,
        incident.AcknowledgedAt, incident.EscalatedAt, incident.ResolvedAt,
        SlaPolicy.AcknowledgeDeadline(incident), incident.SlaBreachedAt,
        [.. incident.Tasks.Select(TaskOut.From)],
        [.. incident.ActivityLogs.Select(ToActivityOut)],
        [.. incident.Participants.Select(p => new UserOut(p.User.Id, p.User.Email, p.User.Name))],
        // Oldest first: a conversation reads top to bottom, unlike the
        // activity feed, which reads newest-first like a notification list.
        [.. incident.Comments.OrderBy(c => c.CreatedAt).Select(CommentOut.From)],
        incident.Commander is null
            ? null
            : new UserOut(
                incident.Commander.Id, incident.Commander.Email,
                incident.Commander.Name, incident.Commander.JobTitle
            )
    );

    // Publishes the whole updated incident plus the activity lines that go
    // with it. Nearly every route below ends with this exact pair of calls,
    // so it lives here once instead of being copy-pasted eight times.
    //
    // ALWAYS called after the transaction has committed, never inside it.
    // Publishing first would mean announcing a change that a failed commit
    // then rolled back, leaving every open page showing something that isn't
    // true until somebody reloads. A publish that fails after a successful
    // commit is the harmless direction to get wrong, and it is handled: see
    // Realtime/RedisEventPublisher.cs, which logs rather than throws.
    private static async Task PublishUpdateAsync(
        IEventPublisher events, int incidentId, IncidentDetail detail, params ActivityLog[] logs
    )
    {
        await events.PublishEventAsync(incidentId, "incident_updated", detail);
        foreach (var log in logs)
        {
            await events.PublishEventAsync(incidentId, "activity_created", ToActivityOut(log));
        }
    }

    // Adds someone to the incident if they aren't already on it. Returns true
    // if a row was actually added, so callers know whether to announce it.
    //
    // Added through the incident's own Participants collection rather than
    // db.IncidentParticipants: that keeps the in-memory list in step with
    // what we are about to write, so calling this twice for the same person
    // in one request correctly does nothing the second time. EF fills in the
    // IncidentId from the relationship.
    private static bool AddParticipantIfMissing(Incident incident, int userId)
    {
        if (incident.Participants.Any(p => p.UserId == userId)) return false;

        incident.Participants.Add(new IncidentParticipant { IncidentId = incident.Id, UserId = userId });
        return true;
    }

    public static void MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/incidents").WithTags("incidents");

        group.MapPost("", async (
            IncidentCreate payload, HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);

            // An incident has to belong to a real team - that is what makes
            // escalation possible later (see the /escalate route below).
            var department = await db.Departments.FirstOrDefaultAsync(d => d.Id == payload.DepartmentId)
                ?? throw ApiException.UnprocessableEntity("Please choose the department this incident belongs to");

            var incident = new Incident
            {
                Title = payload.Title,
                Description = payload.Description,
                Severity = payload.Severity,
                DepartmentId = department.Id,
                CreatedBy = currentUser.Id,
            };

            // One transaction around both saves below. Two saves are
            // unavoidable here - the participant row and the activity line
            // both need the incident's Id, and only the database can hand
            // that out - but a transaction makes the pair atomic, so a
            // failure on the second save rolls the incident back too rather
            // than leaving an incident nobody is on and nothing explains.
            await using var transaction = await db.Database.BeginTransactionAsync();

            db.Incidents.Add(incident);
            await db.SaveChangesAsync(); // assigns incident.Id before we use it below

            // Whoever creates the incident automatically joins it.
            incident.Participants.Add(new IncidentParticipant { IncidentId = incident.Id, UserId = currentUser.Id });
            AddActivity(
                db, incident.Id, currentUser,
                $"{currentUser.Name} created the incident ({department.Name}, {payload.Severity})"
            );
            await db.SaveChangesAsync();

            await transaction.CommitAsync();

            // Re-fetch with all relationships loaded so the response includes them.
            incident = await GetIncidentOr404Async(db, incident.Id);
            return Results.Json(ToDetail(incident), statusCode: StatusCodes.Status201Created);
        });

        group.MapGet("", async (
            HttpContext context, AppDbContext db, JwtService jwt,
            // Filters. All optional - sending none of them gives the same
            // list this endpoint always returned. They are taken as plain
            // strings rather than enums on purpose: a typo like
            // ?status=INVESTIGATNG then produces our own friendly 422 instead
            // of ASP.NET's bodyless 400 from a failed parameter bind.
            [FromQuery(Name = "status")] string? status,
            [FromQuery(Name = "severity")] string? severity,
            [FromQuery(Name = "department_id")] int? departmentId,
            [FromQuery(Name = "q")] string? q,
            [FromQuery(Name = "mine")] bool? mine,
            [FromQuery(Name = "page")] int? page,
            [FromQuery(Name = "page_size")] int? pageSize
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);

            // Two rules applied in order, both defined in Common/IncidentAccess.cs:
            //   VisibleTo   - you only see incidents you're involved in,
            //                 unless you're a leader, who sees them all.
            //   InWorkOrder - OPEN first, then INVESTIGATING, then RESOLVED.
            //
            // Note: this uses the lighter IncidentOut DTO (no tasks/activity/
            // participants) since the list page only needs the summary, not
            // every incident's full detail.
            var query = db.Incidents
                .Include(i => i.Department)
                .VisibleTo(currentUser);

            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!Enum.TryParse<IncidentStatus>(status, ignoreCase: true, out var parsedStatus))
                {
                    throw ApiException.UnprocessableEntity(
                        $"Unknown status. Expected one of: {string.Join(", ", Enum.GetNames<IncidentStatus>())}"
                    );
                }
                query = query.Where(i => i.Status == parsedStatus);
            }

            if (!string.IsNullOrWhiteSpace(severity))
            {
                if (!Enum.TryParse<Severity>(severity, ignoreCase: true, out var parsedSeverity))
                {
                    throw ApiException.UnprocessableEntity(
                        $"Unknown severity. Expected one of: {string.Join(", ", Enum.GetNames<Severity>())}"
                    );
                }
                query = query.Where(i => i.Severity == parsedSeverity);
            }

            if (departmentId is not null)
            {
                query = query.Where(i => i.DepartmentId == departmentId);
            }

            // "Only the ones I'm actually on" - the same rule that decides
            // visibility, reused as a filter. For a leader (who can see
            // everything) this is the switch that cuts the list back down to
            // their own work.
            if (mine == true)
            {
                query = query.Where(IncidentAccess.InvolvedIn(currentUser.Id));
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                // ToLower() on both sides rather than ILIKE: Postgres LIKE is
                // case-sensitive and SQLite's is not, so lowering both sides
                // is the one spelling that behaves identically on the real
                // database and on the SQLite the tests run against.
                var term = q.Trim().ToLower();
                query = query.Where(i =>
                    i.Title.ToLower().Contains(term) || i.Description.ToLower().Contains(term));
            }

            // Pagination. Without it this endpoint returns every incident the
            // company has ever opened, which is fine on day one and a problem
            // by month six. Clamped so a caller cannot ask for page_size=50000.
            var currentPage = Math.Max(page ?? 1, 1);
            var size = Math.Clamp(pageSize ?? 50, 1, 100);

            var incidents = await query
                .InWorkOrder()
                .Skip((currentPage - 1) * size)
                .Take(size)
                .ToListAsync();

            return Results.Ok(incidents.Select(IncidentOut.From));
        });

        group.MapGet("/{incidentId:int}", async (int incidentId, HttpContext context, AppDbContext db, JwtService jwt) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);

            // Hiding an incident from the list would be pointless on its own -
            // anyone could still read it by typing /api/incidents/7 directly -
            // so the same rule is enforced here, where the actual content lives.
            var incident = await RequireVisibleAsync(db, incidentId, currentUser);

            return Results.Ok(ToDetail(incident));
        });

        group.MapPatch("/{incidentId:int}/status", async (
            int incidentId, IncidentStatusUpdate payload,
            HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);
            var incident = await RequireVisibleAsync(db, incidentId, currentUser);

            var oldStatus = incident.Status;
            incident.Status = payload.Status;

            // Resolving stamps the clock; reopening clears it again. Keeping
            // these in step matters because ResolvedAt - CreatedAt is the
            // "time to resolve" number any report would be built on, and an
            // incident that was closed, reopened and closed again should
            // report the time it ACTUALLY took, not the first optimistic guess.
            if (payload.Status == IncidentStatus.RESOLVED)
            {
                incident.ResolvedAt = DateTime.UtcNow;
            }
            else if (oldStatus == IncidentStatus.RESOLVED)
            {
                incident.ResolvedAt = null;
            }

            var log = AddActivity(
                db, incidentId, currentUser,
                $"{currentUser.Name} changed status {oldStatus} → {payload.Status}"
            );

            // ONE save for both the status change and its activity line.
            //
            // No explicit transaction needed: a single SaveChangesAsync is
            // already atomic - EF Core wraps it in its own transaction and
            // either every staged change lands or none does. What used to be
            // wrong here was not the lack of a transaction but the number of
            // saves: writing the status in one and the activity line in
            // another meant a failure in between left the feed disagreeing
            // with the incident it describes. An explicit transaction is only
            // worth the noise where a handler genuinely needs two saves - see
            // the create route above, which needs the first one to get an Id.
            await db.SaveChangesAsync();

            incident = await GetIncidentOr404Async(db, incidentId);
            var detail = ToDetail(incident);

            // This is the real-time part: publish so every other browser tab
            // currently viewing this incident updates instantly, without
            // polling or refreshing. After the commit, always - see
            // PublishUpdateAsync.
            await PublishUpdateAsync(events, incidentId, detail, log);

            return Results.Ok(detail);
        });

        // ---- Acknowledge: "I have seen this and I am on it" ----------------
        //
        // Separate from changing the status to INVESTIGATING, and the
        // difference is what makes it worth measuring: status describes the
        // incident, acknowledgement describes whether a human has arrived
        // yet. The gap between CreatedAt and AcknowledgedAt is MTTA, the
        // number that tells you whether your alerting actually reaches people.
        group.MapPost("/{incidentId:int}/acknowledge", async (
            int incidentId, HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);
            var incident = await RequireVisibleAsync(db, incidentId, currentUser);

            // Idempotent, like joining: acknowledging an incident somebody
            // already acknowledged does nothing rather than overwriting the
            // first responder's name with whoever clicked last.
            if (incident.AcknowledgedAt is not null)
            {
                return Results.Ok(ToDetail(incident));
            }

            var now = DateTime.UtcNow;
            incident.AcknowledgedAt = now;
            incident.AcknowledgedBy = currentUser.Id;

            // Taking an incident on means being on it - otherwise you would
            // have to click "acknowledge" and then "join", and forgetting the
            // second one would cost you access to your own incident.
            AddParticipantIfMissing(incident, currentUser.Id);

            // How long it sat unattended, in the message itself, because that
            // is the detail anyone reading the feed afterwards wants.
            var waited = now - incident.CreatedAt;
            var log = AddActivity(
                db, incidentId, currentUser,
                $"{currentUser.Name} acknowledged the incident after {FormatDuration(waited)}"
            );

            // The timestamp, the participant row and the activity line all
            // land in this one save.
            await db.SaveChangesAsync();

            incident = await GetIncidentOr404Async(db, incidentId);
            var detail = ToDetail(incident);
            await PublishUpdateAsync(events, incidentId, detail, log);

            return Results.Ok(detail);
        });

        // ---- Escalate: pull in the department's senior people --------------
        group.MapPost("/{incidentId:int}/escalate", async (
            int incidentId, HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);
            var incident = await RequireVisibleAsync(db, incidentId, currentUser);

            // Everyone in the owning department, loaded into memory before
            // filtering by level. That is not laziness: Seniority is stored
            // as TEXT (see Data/AppDbContext.cs), so "level >= LEAD" cannot
            // be asked of the database without comparing words alphabetically
            // - "DIRECTOR" < "JUNIOR" - which is nonsense. One department is
            // a few dozen rows, so doing the ranking in C# is cheap and correct.
            var departmentMembers = await db.Users
                .Where(u => u.DepartmentId == incident.DepartmentId)
                .ToListAsync();

            var seniorPeople = departmentMembers
                .Where(u => u.Seniority >= IncidentAccess.EscalationLevel)
                .OrderByDescending(u => u.Seniority)
                .ThenBy(u => u.Name)
                .ToList();

            if (seniorPeople.Count == 0)
            {
                // The seeded company guarantees every department has somebody
                // at LEAD or above, so this only happens for a department
                // somebody added by hand. Say so plainly rather than
                // pretending the escalation worked.
                throw ApiException.UnprocessableEntity(
                    "Nobody in this incident's department is senior enough to escalate to"
                );
            }

            var added = seniorPeople.Where(u => AddParticipantIfMissing(incident, u.Id)).ToList();

            incident.EscalatedAt = DateTime.UtcNow;

            // Name the first few rather than printing a wall of twelve names.
            var names = added.Count == 0
                ? "everyone senior was already on it"
                : string.Join(", ", added.Take(3).Select(u => u.Name))
                  + (added.Count > 3 ? $" and {added.Count - 3} more" : "");

            var log = AddActivity(
                db, incidentId, currentUser,
                $"{currentUser.Name} escalated the incident - {names}"
            );

            // Every new participant, the escalation timestamp and the
            // activity line, in one save: either the whole escalation
            // happened or none of it did. Half an escalation - three of five
            // leads added, no record of why - is the worst possible outcome.
            await db.SaveChangesAsync();

            incident = await GetIncidentOr404Async(db, incidentId);
            var detail = ToDetail(incident);

            await PublishUpdateAsync(events, incidentId, detail, log);

            // One user_joined event per person pulled in, so an open page
            // updates its participant list the same way it does for a normal
            // join rather than waiting for a refresh.
            foreach (var person in added)
            {
                await events.PublishEventAsync(
                    incidentId, "user_joined",
                    new { user = new UserOut(person.Id, person.Email, person.Name, person.JobTitle) }
                );
            }

            return Results.Ok(detail);
        });

        // ---- Incident Commander: one name, visible to everyone -------------
        group.MapPatch("/{incidentId:int}/commander", async (
            int incidentId, IncidentCommanderUpdate payload,
            HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);
            var incident = await RequireVisibleAsync(db, incidentId, currentUser);

            string message;
            User? newCommander = null;

            if (payload.UserId is null)
            {
                // Clearing the role. Nothing to look up - but refuse if there
                // was no commander to begin with, so the feed doesn't collect
                // "X stood down" lines from people who never had the job.
                if (incident.CommanderUserId is null)
                {
                    throw ApiException.BadRequest("This incident has no commander to stand down");
                }

                incident.CommanderUserId = null;
                message = $"{currentUser.Name} cleared the incident commander";
            }
            else
            {
                newCommander = await db.Users.FirstOrDefaultAsync(u => u.Id == payload.UserId)
                    ?? throw ApiException.UnprocessableEntity("That user does not exist");

                incident.CommanderUserId = newCommander.Id;

                // The commander has to be able to open the incident they are
                // running, so put them on it if they aren't already.
                AddParticipantIfMissing(incident, newCommander.Id);

                // "took command" when it's yourself reads better than
                // "Dana made Dana the incident commander".
                message = newCommander.Id == currentUser.Id
                    ? $"{currentUser.Name} took command of the incident"
                    : $"{currentUser.Name} made {newCommander.Name} the incident commander";
            }

            var log = AddActivity(db, incidentId, currentUser, message);
            await db.SaveChangesAsync();

            incident = await GetIncidentOr404Async(db, incidentId);
            var detail = ToDetail(incident);
            await PublishUpdateAsync(events, incidentId, detail, log);

            if (newCommander is not null)
            {
                await events.PublishEventAsync(
                    incidentId, "user_joined",
                    new { user = new UserOut(newCommander.Id, newCommander.Email, newCommander.Name, newCommander.JobTitle) }
                );
            }

            return Results.Ok(detail);
        });

        group.MapDelete("/{incidentId:int}", async (
            int incidentId, HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);
            var incident = await GetIncidentOr404Async(db, incidentId);

            // Only the person who opened the incident can delete it. Without
            // this check, anyone with an account could wipe out someone
            // else's incident - and deleting takes its whole task list and
            // activity history with it, so there is nothing to undo.
            if (incident.CreatedBy != currentUser.Id)
            {
                throw ApiException.Forbidden("Only the person who created this incident can delete it");
            }

            // Removing the incident also removes its tasks, comments,
            // activity logs, and participant rows - that is the
            // DeleteBehavior.Cascade configured in Data/AppDbContext.cs doing
            // the work, so we don't have to delete the children by hand.
            db.Incidents.Remove(incident);
            await db.SaveChangesAsync();

            // Announced only once the row is actually gone.
            //
            // This used to be published BEFORE the delete, on the reasoning
            // that an event could not then be lost if the delete threw. That
            // had it backwards: if the delete failed, every open page had
            // already removed an incident that still existed, and the only
            // way to find out was to reload. Telling people something was
            // deleted when it wasn't is far worse than a missed notification
            // about something that really was.
            await events.PublishEventAsync(incidentId, "incident_deleted", new { id = incidentId });

            // 204 No Content is the conventional reply to a successful
            // DELETE: it succeeded, and there is deliberately no body to
            // send back (the thing you asked about no longer exists).
            return Results.NoContent();
        });

        group.MapPost("/{incidentId:int}/join", async (
            int incidentId, HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);

            // Deliberately NOT RequireVisibleAsync: joining is how somebody
            // who is not yet involved gets involved, so demanding access
            // first would make the button impossible to use.
            var incident = await GetIncidentOr404Async(db, incidentId);

            // Idempotent: joining an incident you're already part of is a
            // no-op instead of creating a duplicate participant row or a
            // spammy activity log.
            if (AddParticipantIfMissing(incident, currentUser.Id))
            {
                var log = AddActivity(db, incidentId, currentUser, $"{currentUser.Name} joined the incident");

                // Participant row and activity line together, then publish.
                await db.SaveChangesAsync();

                await events.PublishEventAsync(incidentId, "activity_created", ToActivityOut(log));
                await events.PublishEventAsync(
                    incidentId, "user_joined",
                    new { user = new UserOut(currentUser.Id, currentUser.Email, currentUser.Name) }
                );
            }

            incident = await GetIncidentOr404Async(db, incidentId);
            return Results.Ok(ToDetail(incident));
        });
    }

    // "2 minutes" / "1 hour 5 minutes" - for the activity feed, where
    // "00:02:13.4821" would be noise. Kept deliberately rough: nobody reading
    // an incident timeline cares about the seconds after the first minute.
    private static string FormatDuration(TimeSpan span)
    {
        if (span.TotalMinutes < 1) return $"{Math.Max((int)span.TotalSeconds, 0)} seconds";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} minutes";

        var hours = (int)span.TotalHours;
        var minutes = span.Minutes;
        var hourText = hours == 1 ? "1 hour" : $"{hours} hours";
        return minutes == 0 ? hourText : $"{hourText} {minutes} minutes";
    }
}
