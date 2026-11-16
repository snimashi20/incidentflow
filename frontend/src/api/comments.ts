// Wrappers around the incident conversation endpoints - see
// backend/IncidentFlow.Api/Endpoints/CommentEndpoints.cs.
//
// Note what is missing: there is no "edit comment" call, because the backend
// deliberately has no such endpoint. An incident timeline you can quietly
// rewrite afterwards is worth much less when you are trying to work out what
// actually happened.
import { api } from "./client";
import type { Comment } from "../types";

export function postComment(incidentId: number, body: string): Promise<Comment> {
  return api.post<Comment>(`/api/incidents/${incidentId}/comments`, { body });
}

// Only works on your own comments - the backend answers 403 for anyone
// else's, so callers should be ready to show that message.
export function deleteComment(commentId: number): Promise<void> {
  return api.del(`/api/comments/${commentId}`);
}
