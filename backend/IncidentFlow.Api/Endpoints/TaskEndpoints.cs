// Task endpoints: create a task under an incident, and update a task's
// title/status/assignee. Like IncidentEndpoints, every change here also logs
// an activity entry and publishes a WebSocket event so it shows up live for
// everyone else.
using IncidentFlow.Api.Common;
using IncidentFlow.Api.Data;
using IncidentFlow.Api.Dtos;
using IncidentFlow.Api.Entities;
using IncidentFlow.Api.Realtime;
using IncidentFlow.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Api.Endpoints;

public static class TaskEndpoints
{
    // Loads an incident AND checks the caller is allowed to touch it.
    //
    // Both routes below go through this. Until they did, these were the two
    // widest holes in the app: anyone with an account could add a task to any
    // incident by id, or edit any task by id, on incidents they were never
    // allowed to open. Worse, adding a task and assigning it to yourself was
    // a way to GRANT yourself access to that incident, since being assigned
    // a task is one of the things that makes one visible to you (see
    // Common/IncidentAccess.cs).
    //
    // Only Participants and Tasks are loaded, because those are the only two
    // collections the visibility rule reads - no point dragging in the
    // comments and activity log to answer a yes/no question.
    private static async Task<Incident> RequireVisibleIncidentAsync(AppDbContext db, int incidentId, User user)
    {
        var incident = await db.Incidents
            .Include(i => i.Participants)
            .Include(i => i.Tasks)
            .FirstOrDefaultAsync(i => i.Id == incidentId)
            ?? throw ApiException.NotFound("Incident not found");

        if (!IncidentAccess.CanSee(incident, user))
        {
            throw ApiException.Forbidden("You do not have access to this incident");
        }

        return incident;
    }

    private static async Task<TaskItem> GetTaskOr404Async(AppDbContext db, int taskId) =>
        await db.Tasks.FindAsync(taskId) ?? throw ApiException.NotFound("Task not found");

    // Checks that an assignee id refers to a real person, and returns them.
    //
    // Worth having as its own helper because it is easy to get half-right:
    // the update route below always looked the user up (it needed their name
    // for the activity message), but the create route just stored whatever
    // integer arrived. So POSTing assigned_to: 9999 produced a task assigned
    // to nobody, which the UI then rendered as "Assign..." - indistinguishable
    // from an unassigned task, and quietly dependent on the database's
    // foreign key to be the thing that complained.
    private static async Task<User> GetAssigneeOr422Async(AppDbContext db, int userId) =>
        await db.Users.FindAsync(userId)
        ?? throw ApiException.UnprocessableEntity("Cannot assign this task: that user does not exist");

    // Stages one activity line. Deliberately does not save - see the longer
    // explanation on the identical helper in IncidentEndpoints.cs.
    private static ActivityLog AddActivity(AppDbContext db, int incidentId, User user, string message)
    {
        var log = new ActivityLog { IncidentId = incidentId, UserId = user.Id, User = user, Message = message };
        db.ActivityLogs.Add(log);
        return log;
    }

    private static ActivityOut ToActivityOut(ActivityLog log) =>
        new(log.Id, log.Message, log.CreatedAt, new UserOut(log.User.Id, log.User.Email, log.User.Name));

    // No shared prefix here (unlike IncidentEndpoints) because tasks live
    // under two different URL shapes: /api/incidents/{id}/tasks (create) and
    // /api/tasks/{id} (update).
    public static void MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/incidents/{incidentId:int}/tasks", async (
            int incidentId, TaskCreate payload,
            HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);

            // 404 if the incident doesn't exist, 403 if it isn't yours to add
            // work to. Checked before anything else, so a caller who should
            // not be here learns nothing about the incident from the reply.
            await RequireVisibleIncidentAsync(db, incidentId, currentUser);

            // Validated before the insert, so a bad id is a clear 422 rather
            // than a foreign-key violation surfacing as a 500.
            User? assignee = null;
            if (payload.AssignedTo is not null)
            {
                assignee = await GetAssigneeOr422Async(db, payload.AssignedTo.Value);
            }

            var task = new TaskItem { IncidentId = incidentId, Title = payload.Title, AssignedTo = payload.AssignedTo };
            db.Tasks.Add(task);

            var log = AddActivity(
                db, incidentId, currentUser,
                assignee is null
                    ? $"{currentUser.Name} added task \"{task.Title}\""
                    : $"{currentUser.Name} added task \"{task.Title}\" and assigned it to {assignee.Name}"
            );

            // One save for the task and its activity line. Previously these
            // were two separate saves, so a failure in between left a task
            // with no record of who created it.
            await db.SaveChangesAsync();

            // Published after the write, never before.
            await events.PublishEventAsync(incidentId, "task_created", TaskOut.From(task));
            await events.PublishEventAsync(incidentId, "activity_created", ToActivityOut(log));

            return Results.Json(TaskOut.From(task), statusCode: StatusCodes.Status201Created);
        }).WithTags("tasks");

        app.MapPatch("/api/tasks/{taskId:int}", async (
            int taskId, TaskUpdate payload,
            HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);
            var task = await GetTaskOr404Async(db, taskId);

            // A task is only editable if its incident is visible to you.
            //
            // Note who this still lets through, deliberately: the person the
            // task is assigned to. Being given work on an incident is one of
            // the ways it becomes visible to you, so an assignee can tick
            // their own task off without having to be added as a participant
            // first - which is the whole point of that rule.
            await RequireVisibleIncidentAsync(db, task.IncidentId, currentUser);

            // TaskUpdate's fields are all optional, so a PATCH request might
            // only touch one of title/status/assignedTo. We build up a list
            // of human-readable activity messages only for the fields that
            // actually changed.
            var messages = new List<string>();

            if (payload.Title is not null)
            {
                task.Title = payload.Title;
            }

            if (payload.Status is not null && payload.Status != task.Status)
            {
                messages.Add($"{currentUser.Name} changed task \"{task.Title}\" status → {payload.Status}");
                task.Status = payload.Status.Value;
            }

            if (payload.AssignedTo is not null && payload.AssignedTo != task.AssignedTo)
            {
                // Same validation as the create route. This used to silently
                // skip the activity message when the user did not exist,
                // which meant reassigning a task to a nonexistent id looked
                // like it had worked but left no trace of it in the feed.
                var assignee = await GetAssigneeOr422Async(db, payload.AssignedTo.Value);

                task.AssignedTo = assignee.Id;
                messages.Add($"{currentUser.Name} assigned \"{task.Title}\" to {assignee.Name}");
            }

            var logs = messages.Select(message => AddActivity(db, task.IncidentId, currentUser, message)).ToList();

            // The task's changes and every activity line describing them, in
            // one save.
            await db.SaveChangesAsync();

            await events.PublishEventAsync(task.IncidentId, "task_updated", TaskOut.From(task));
            foreach (var log in logs)
            {
                await events.PublishEventAsync(task.IncidentId, "activity_created", ToActivityOut(log));
            }

            return Results.Ok(TaskOut.From(task));
        }).WithTags("tasks");
    }
}
