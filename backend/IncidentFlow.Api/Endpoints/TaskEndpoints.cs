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
    private static async Task<Incident> GetIncidentOr404Async(AppDbContext db, int incidentId) =>
        await db.Incidents.FindAsync(incidentId) ?? throw ApiException.NotFound("Incident not found");

    private static async Task<TaskItem> GetTaskOr404Async(AppDbContext db, int taskId) =>
        await db.Tasks.FindAsync(taskId) ?? throw ApiException.NotFound("Task not found");

    private static async Task<ActivityLog> LogActivityAsync(AppDbContext db, int incidentId, User user, string message)
    {
        var log = new ActivityLog { IncidentId = incidentId, UserId = user.Id, User = user, Message = message };
        db.ActivityLogs.Add(log);
        await db.SaveChangesAsync();
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
            await GetIncidentOr404Async(db, incidentId); // 404 early if the incident doesn't exist

            var task = new TaskItem { IncidentId = incidentId, Title = payload.Title, AssignedTo = payload.AssignedTo };
            db.Tasks.Add(task);
            await db.SaveChangesAsync();

            var log = await LogActivityAsync(db, incidentId, currentUser, $"{currentUser.Name} added task \"{task.Title}\"");

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
                task.AssignedTo = payload.AssignedTo;
                var assignee = await db.Users.FindAsync(payload.AssignedTo.Value);
                if (assignee is not null)
                {
                    messages.Add($"{currentUser.Name} assigned \"{task.Title}\" to {assignee.Name}");
                }
            }

            await db.SaveChangesAsync();

            var logs = new List<ActivityLog>();
            foreach (var message in messages)
            {
                logs.Add(await LogActivityAsync(db, task.IncidentId, currentUser, message));
            }

            await events.PublishEventAsync(task.IncidentId, "task_updated", TaskOut.From(task));
            foreach (var log in logs)
            {
                await events.PublishEventAsync(task.IncidentId, "activity_created", ToActivityOut(log));
            }

            return Results.Ok(TaskOut.From(task));
        }).WithTags("tasks");
    }
}
