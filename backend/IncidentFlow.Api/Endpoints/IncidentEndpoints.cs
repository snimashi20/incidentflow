// Incident endpoints: create, list, view, change status, and join. Every
// mutating route here also does two extra things beyond the database write:
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
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Api.Endpoints;

public static class IncidentEndpoints
{
    // Fetches one incident together with its tasks, activity log, and
    // participants in a small, fixed number of extra queries (EF Core calls
    // this "eager loading" via Include/ThenInclude) rather than one query per
    // related row, which would get slow as an incident grows more tasks/logs.
    private static async Task<Incident> GetIncidentOr404Async(AppDbContext db, int incidentId)
    {
        var incident = await db.Incidents
            .Include(i => i.Tasks)
            .Include(i => i.ActivityLogs).ThenInclude(a => a.User)
            .Include(i => i.Participants).ThenInclude(p => p.User)
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == incidentId);

        return incident ?? throw ApiException.NotFound("Incident not found");
    }

    // Small helper used by every route below to add one line to the activity
    // feed. We already have the User object in hand, so we just attach it to
    // the navigation property directly instead of reloading it from the database.
    private static async Task<ActivityLog> LogActivityAsync(AppDbContext db, int incidentId, User user, string message)
    {
        var log = new ActivityLog { IncidentId = incidentId, UserId = user.Id, User = user, Message = message };
        db.ActivityLogs.Add(log);
        await db.SaveChangesAsync();
        return log;
    }

    private static ActivityOut ToActivityOut(ActivityLog log) =>
        new(log.Id, log.Message, log.CreatedAt, new UserOut(log.User.Id, log.User.Email, log.User.Name));

    // Converts an EF Core Incident (database row + relationships) into the
    // IncidentDetail DTO we actually send over the API/WebSocket.
    private static IncidentDetail ToDetail(Incident incident) => new(
        incident.Id, incident.Title, incident.Description, incident.Severity, incident.Status,
        incident.CreatedBy, incident.CreatedAt, incident.UpdatedAt,
        [.. incident.Tasks.Select(TaskOut.From)],
        [.. incident.ActivityLogs.Select(ToActivityOut)],
        [.. incident.Participants.Select(p => new UserOut(p.User.Id, p.User.Email, p.User.Name))]
    );

    public static void MapIncidentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/incidents").WithTags("incidents");

        group.MapPost("", async (
            IncidentCreate payload, HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);

            var incident = new Incident
            {
                Title = payload.Title,
                Description = payload.Description,
                Severity = payload.Severity,
                CreatedBy = currentUser.Id,
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(); // assigns incident.Id before we use it below

            // Whoever creates the incident automatically joins it.
            db.IncidentParticipants.Add(new IncidentParticipant { IncidentId = incident.Id, UserId = currentUser.Id });
            await LogActivityAsync(db, incident.Id, currentUser, $"{currentUser.Name} created the incident");
            await db.SaveChangesAsync();

            // Re-fetch with all relationships loaded so the response includes them.
            incident = await GetIncidentOr404Async(db, incident.Id);
            return Results.Json(ToDetail(incident), statusCode: StatusCodes.Status201Created);
        });

        group.MapGet("", async (HttpContext context, AppDbContext db, JwtService jwt) =>
        {
            await CurrentUser.RequireAsync(context, db, jwt);

            // Note: this uses the lighter IncidentOut DTO (no tasks/activity/
            // participants) since the list page only needs the summary, not
            // every incident's full detail.
            var incidents = await db.Incidents.OrderByDescending(i => i.CreatedAt).ToListAsync();
            return Results.Ok(incidents.Select(IncidentOut.From));
        });

        group.MapGet("/{incidentId:int}", async (int incidentId, HttpContext context, AppDbContext db, JwtService jwt) =>
        {
            await CurrentUser.RequireAsync(context, db, jwt);
            var incident = await GetIncidentOr404Async(db, incidentId);
            return Results.Ok(ToDetail(incident));
        });

        group.MapPatch("/{incidentId:int}/status", async (
            int incidentId, IncidentStatusUpdate payload,
            HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);
            var incident = await GetIncidentOr404Async(db, incidentId);

            var oldStatus = incident.Status;
            incident.Status = payload.Status;

            var log = await LogActivityAsync(
                db, incidentId, currentUser,
                $"{currentUser.Name} changed status {oldStatus} → {payload.Status}"
            );
            await db.SaveChangesAsync();

            incident = await GetIncidentOr404Async(db, incidentId);
            var detail = ToDetail(incident);

            // This is the real-time part: publish two events so every other
            // browser tab currently viewing this incident updates instantly,
            // without polling or refreshing.
            await events.PublishEventAsync(incidentId, "incident_updated", detail);
            await events.PublishEventAsync(incidentId, "activity_created", ToActivityOut(log));

            return Results.Ok(detail);
        });

        group.MapPost("/{incidentId:int}/join", async (
            int incidentId, HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);
            var incident = await GetIncidentOr404Async(db, incidentId);

            // Idempotent: joining an incident you're already part of is a
            // no-op instead of creating a duplicate participant row or a
            // spammy activity log.
            var alreadyJoined = incident.Participants.Any(p => p.UserId == currentUser.Id);
            if (!alreadyJoined)
            {
                db.IncidentParticipants.Add(new IncidentParticipant { IncidentId = incidentId, UserId = currentUser.Id });
                var log = await LogActivityAsync(db, incidentId, currentUser, $"{currentUser.Name} joined the incident");
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
}
