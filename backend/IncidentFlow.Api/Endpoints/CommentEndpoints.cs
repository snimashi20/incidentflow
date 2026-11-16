// The incident conversation: what people say to each other while working on
// an incident, as opposed to what the app records about them (that is the
// activity feed - see Entities/Comment.cs for why the two are separate).
//
// Comments are deliberately thin: post one, delete your own. There is no
// editing, because an incident timeline that can be quietly rewritten
// afterwards is worth a lot less when you are trying to work out what
// actually happened.
using IncidentFlow.Api.Common;
using IncidentFlow.Api.Data;
using IncidentFlow.Api.Dtos;
using IncidentFlow.Api.Entities;
using IncidentFlow.Api.Realtime;
using IncidentFlow.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Api.Endpoints;

public static class CommentEndpoints
{
    // Long enough for a paste of a stack trace, short enough that nobody can
    // push a megabyte of text into the database through the comment box.
    private const int MaxBodyLength = 4000;

    public static void MapCommentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/incidents/{incidentId:int}/comments", async (
            int incidentId, CommentCreate payload,
            HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);

            // You can only talk on an incident you are allowed to read. The
            // same visibility rule as everywhere else, from
            // Common/IncidentAccess.cs - loaded with the two collections that
            // rule needs (participants and tasks) and nothing more, since a
            // comment does not care about the rest of the incident.
            var incident = await db.Incidents
                .Include(i => i.Participants)
                .Include(i => i.Tasks)
                .FirstOrDefaultAsync(i => i.Id == incidentId)
                ?? throw ApiException.NotFound("Incident not found");

            if (!IncidentAccess.CanSee(incident, currentUser))
            {
                throw ApiException.Forbidden("You do not have access to this incident");
            }

            var body = payload.Body?.Trim() ?? string.Empty;

            if (body.Length == 0)
            {
                throw ApiException.UnprocessableEntity("A comment cannot be empty");
            }

            if (body.Length > MaxBodyLength)
            {
                throw ApiException.UnprocessableEntity($"A comment cannot be longer than {MaxBodyLength} characters");
            }

            // User is attached directly rather than looked up again - we
            // already have the object, and CommentOut.From needs it to put a
            // name on the message.
            var comment = new Comment
            {
                IncidentId = incidentId,
                UserId = currentUser.Id,
                User = currentUser,
                Body = body,
            };
            db.Comments.Add(comment);
            await db.SaveChangesAsync();

            var dto = CommentOut.From(comment);

            // Note what is NOT here: no activity log entry. A comment is
            // already visible in the thread, and announcing "Dana commented"
            // next to Dana's comment would just be the same news twice.
            await events.PublishEventAsync(incidentId, "comment_created", dto);

            return Results.Json(dto, statusCode: StatusCodes.Status201Created);
        }).WithTags("comments");

        app.MapDelete("/api/comments/{commentId:int}", async (
            int commentId, HttpContext context, AppDbContext db, JwtService jwt, IEventPublisher events
        ) =>
        {
            var currentUser = await CurrentUser.RequireAsync(context, db, jwt);

            var comment = await db.Comments.FirstOrDefaultAsync(c => c.Id == commentId)
                ?? throw ApiException.NotFound("Comment not found");

            // Only your own words are yours to take back. Note this is a
            // stricter rule than visibility: being able to READ a thread does
            // not let you delete other people's messages in it.
            if (comment.UserId != currentUser.Id)
            {
                throw ApiException.Forbidden("You can only delete your own comments");
            }

            var incidentId = comment.IncidentId;

            db.Comments.Remove(comment);
            await db.SaveChangesAsync();

            // The id is enough for open pages to drop it from the thread -
            // there is no comment left to describe.
            await events.PublishEventAsync(incidentId, "comment_deleted", new { id = commentId, incident_id = incidentId });

            return Results.NoContent();
        }).WithTags("comments");
    }
}
