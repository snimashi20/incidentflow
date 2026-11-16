namespace IncidentFlow.Api.Dtos;

// What someone types into the comment box. Just the text - who wrote it and
// when are decided by the server from the access token and the clock, never
// taken from the client (otherwise anyone could post as anyone else).
public record CommentCreate(string Body);

public record CommentOut(int Id, int IncidentId, string Body, DateTime CreatedAt, UserOut User)
{
    public static CommentOut From(Entities.Comment comment) => new(
        comment.Id,
        comment.IncidentId,
        comment.Body,
        comment.CreatedAt,
        // The short form of UserOut (no department): a comment only ever
        // shows a name and a job title, so there is no reason to make the
        // database join the department table for every message in the thread.
        new UserOut(comment.User.Id, comment.User.Email, comment.User.Name, comment.User.JobTitle)
    );
}
