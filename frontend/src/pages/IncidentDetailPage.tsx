// This is the most important page in the app - it's the one that actually
// shows the real-time behaviour: status, tasks, comments, who is watching,
// and team activity all updating live. It combines:
//   1. A normal initial fetch (getIncident) to load the page the first time.
//   2. A WebSocket subscription (useIncidentSocket) that keeps the page in
//      sync afterwards, whenever ANYONE (including other browser tabs/users)
//      changes something about this incident.
import { FormEvent, useEffect, useMemo, useState } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import {
  acknowledgeIncident,
  createTask,
  deleteIncident,
  escalateIncident,
  getIncident,
  joinIncident,
  listUsers,
  setIncidentCommander,
  updateIncidentStatus,
  updateTask,
} from "../api/incidents";
import { deleteComment, postComment } from "../api/comments";
import { ApiError } from "../api/client";
import { SeverityBadge, SlaBadge, StatusBadge, TaskStatusIcon } from "../components/Badges";
import { useIncidentSocket } from "../hooks/useIncidentSocket";
import { useAuth } from "../context/AuthContext";
import { bySeniorityThenName } from "../lib/seniority";
import { useNow } from "../lib/useNow";
import type {
  ActivityLogEntry,
  Comment,
  IncidentDetail as IncidentDetailType,
  IncidentStatus,
  Task,
  User,
  Viewer,
  WsEvent,
} from "../types";

const STATUS_OPTIONS: IncidentStatus[] = ["OPEN", "INVESTIGATING", "RESOLVED"];

export function IncidentDetailPage() {
  // useParams reads the `:id` part of the URL (e.g. /incidents/1 -> id = "1"),
  // as defined in App.tsx's route configuration.
  const { id } = useParams<{ id: string }>();
  const incidentId = Number(id);
  const { user } = useAuth();
  // useNavigate gives us a function to change pages from inside an event
  // handler (as opposed to <Link>, which the user has to click).
  const navigate = useNavigate();
  const now = useNow(); // ticks, so the SLA countdown doesn't freeze

  const [incident, setIncident] = useState<IncidentDetailType | null>(null);
  const [users, setUsers] = useState<User[]>([]);
  const [newTaskTitle, setNewTaskTitle] = useState("");
  const [newComment, setNewComment] = useState("");
  // Who currently has this page open. Lives outside `incident` because it
  // is not part of the incident at all - nothing about it is stored, it is
  // purely "who is connected right now".
  const [viewers, setViewers] = useState<Viewer[]>([]);
  // Set when SOMEONE ELSE deletes this incident while we're looking at it.
  // We show a message instead of silently yanking the page away, so the
  // reader understands why the incident they were reading vanished.
  const [deletedByOther, setDeletedByOther] = useState(false);
  // Holds the message from any rejected action (a 403 for deleting someone
  // else's comment, a 422 for escalating a department with nobody senior
  // in it), so the refusal is shown rather than swallowed.
  const [actionError, setActionError] = useState<string | null>(null);
  // Set when the incident couldn't be loaded at all - most often because
  // the backend refused it (403), not because anything broke.
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    // Load this incident's full detail, and the list of users (for the
    // "assign task" and "commander" dropdowns), once when the page opens.
    //
    // The .catch matters here: the backend returns 403 if you aren't
    // involved in this incident and aren't a leader. Without handling it,
    // the rejected promise would go unnoticed and the page would sit on
    // "Loading..." forever, which looks like a bug rather than a refusal.
    getIncident(incidentId)
      .then(setIncident)
      .catch((err) =>
        setLoadError(
          err instanceof ApiError && err.status === 403
            ? "You do not have access to this incident. You will see it here once you are assigned a task on it or you join it."
            : "Could not load this incident.",
        ),
      );
    listUsers().then(setUsers);
  }, [incidentId]);

  // useMemo recalculates this only when `incident` or `user` actually change,
  // instead of on every single render - here it's cheap either way, but it's
  // a common React pattern worth knowing.
  const hasJoined = useMemo(
    () => incident?.participants.some((p) => p.id === user?.id) ?? false,
    [incident, user],
  );

  // Only the person who opened the incident may delete it, so everyone else
  // never even sees the button. The backend enforces the same rule itself
  // (see MapDelete in Endpoints/IncidentEndpoints.cs) - hiding a button is a
  // UI convenience, never a security measure, since anyone can call the API
  // directly without going through our page.
  const isCreator = incident?.created_by === user?.id;

  // Group the employee list by department for the "assign task" and
  // "commander" dropdowns. With 75 people across 14 departments, one flat
  // alphabetical list is useless - what you actually want to see is "these
  // are the Cloud engineers", so a database outage never gets assigned to an
  // accountant by a mis-click. Returns [departmentName, members][] in name
  // order. Within each department the people are ordered most senior first,
  // so the VP / director / tech lead you would escalate to is at the top of
  // their group instead of buried wherever the alphabet put them.
  const usersByDepartment = useMemo(() => {
    const groups = new Map<string, User[]>();
    for (const u of users) {
      const departmentName = u.department?.name ?? "No department";
      // "Get the existing group, or start a new empty one" - the standard
      // way to build a grouped Map in JavaScript.
      const group = groups.get(departmentName) ?? [];
      group.push(u);
      groups.set(departmentName, group);
    }
    for (const group of groups.values()) {
      group.sort(bySeniorityThenName);
    }
    return [...groups.entries()].sort(([a], [b]) => a.localeCompare(b));
  }, [users]);

  // This is where the live updates actually happen. useIncidentSocket opens
  // a WebSocket and calls this function every time the backend broadcasts an
  // event for this incident (see Endpoints/IncidentEndpoints.cs,
  // CommentEndpoints.cs and Realtime/ on the backend for where these events
  // get published).
  useIncidentSocket(
    incidentId,
    (event: WsEvent) => {
      // Two events are handled outside the setIncident call below, because
      // neither of them describes a change TO the incident.
      if (event.type === "incident_deleted") {
        setDeletedByOther(true);
        return;
      }

      if (event.type === "presence_updated") {
        setViewers((event.data as { viewers: Viewer[] }).viewers);
        return;
      }

      setIncident((prev) => {
        if (!prev) return prev;

        // Each event `type` updates the local state differently - this is
        // effectively a small reducer, just written as a switch instead of
        // useReducer. We merge into the existing incident object rather than
        // re-fetching from the server, so the update feels instant.
        switch (event.type) {
          case "incident_updated":
            // The backend sends the WHOLE updated incident for this one, so we
            // can just replace our local copy entirely.
            return event.data as IncidentDetailType;
          case "task_created": {
            const task = event.data as Task;
            // Guard against adding the same task twice - e.g. if the person
            // who created it also receives their own broadcast back.
            if (prev.tasks.some((t) => t.id === task.id)) return prev;
            return { ...prev, tasks: [...prev.tasks, task] };
          }
          case "task_updated": {
            const task = event.data as Task;
            return { ...prev, tasks: prev.tasks.map((t) => (t.id === task.id ? task : t)) };
          }
          case "activity_created": {
            const log = event.data as ActivityLogEntry;
            if (prev.activity_logs.some((l) => l.id === log.id)) return prev;
            return { ...prev, activity_logs: [log, ...prev.activity_logs] };
          }
          case "user_joined": {
            const joined = (event.data as { user: User }).user;
            if (prev.participants.some((p) => p.id === joined.id)) return prev;
            return { ...prev, participants: [...prev.participants, joined] };
          }
          case "comment_created": {
            const comment = event.data as Comment;
            // Same de-duplication as tasks: the person who posted it receives
            // their own broadcast back, and it is already on screen.
            if (prev.comments.some((c) => c.id === comment.id)) return prev;
            return { ...prev, comments: [...prev.comments, comment] };
          }
          case "comment_deleted": {
            const { id: deletedId } = event.data as { id: number };
            return { ...prev, comments: prev.comments.filter((c) => c.id !== deletedId) };
          }
          case "sla_breached": {
            // Published by the backend's background monitor rather than by a
            // person. It carries only the breach fields, so merge them in
            // instead of replacing the whole incident.
            const breach = event.data as { sla_breached_at: string };
            return { ...prev, sla_breached_at: breach.sla_breached_at };
          }
          default:
            return prev;
        }
      });
    },
    // Called when the socket comes back after a drop, never on the first
    // connection. Everything that happened while we were disconnected was
    // broadcast to nobody and is not replayable, so the only honest way to
    // catch up is to ask the server for the incident again. Without this the
    // page would reconnect, look perfectly healthy, and quietly be missing
    // every comment and task from the gap.
    () => {
      getIncident(incidentId)
        .then(setIncident)
        .catch(() => {
          // Reconnected but can't read the incident any more - most likely
          // somebody deleted it while we were away, which the socket's own
          // "incident_deleted" event never reached us to say.
          setDeletedByOther(true);
        });
    },
  );

  // A small wrapper so every button below gets the same treatment: run the
  // call, show the incident it returns, and surface the backend's own
  // message if it refuses. Without this, each handler would repeat the
  // identical try/catch five times.
  async function runAction(action: () => Promise<IncidentDetailType>) {
    try {
      setIncident(await action());
      setActionError(null);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "That didn't work. Please try again.");
    }
  }

  const handleJoin = () => runAction(() => joinIncident(incidentId));

  const handleAcknowledge = () => runAction(() => acknowledgeIncident(incidentId));

  function handleEscalate() {
    // Escalating pages real people - a tech lead's evening, possibly a
    // director's - so it asks first. Everything else on this page is
    // reversible; pulling five senior engineers onto an incident is not.
    const confirmed = window.confirm(
      `Escalate "${incident!.title}"?\n\n` +
        `This adds everyone at tech-lead level or above in ${incident!.department_name ?? "this department"} ` +
        `to the incident.`,
    );
    if (!confirmed) return;

    runAction(() => escalateIncident(incidentId));
  }

  const handleSetCommander = (userId: number | null) =>
    runAction(() => setIncidentCommander(incidentId, userId));

  async function handleDelete() {
    // Deleting takes the incident's tasks, comments and whole activity
    // history with it and there is no undo, so we ask first. window.confirm
    // is the browser's built-in blocking dialog - a real product would use a
    // styled modal, but the safety behaviour is what matters here.
    const confirmed = window.confirm(
      `Delete "${incident!.title}"?\n\n` +
        `This also deletes its ${incident!.tasks.length} task(s), ` +
        `${incident!.comments.length} comment(s) and ` +
        `${incident!.activity_logs.length} activity log entries. This cannot be undone.`,
    );
    if (!confirmed) return;

    try {
      await deleteIncident(incidentId);
      // `replace: true` swaps this page out of the browser's history
      // instead of stacking on top of it, so pressing Back doesn't return
      // the user to the detail page of an incident that no longer exists.
      navigate("/incidents", { replace: true });
    } catch (err) {
      // Most likely a 403 (not your incident) or a 404 (someone else
      // already deleted it a moment ago). Either way, show the backend's
      // own message rather than failing silently.
      setActionError(err instanceof ApiError ? err.message : "Could not delete this incident.");
    }
  }

  async function handleStatusChange(status: IncidentStatus) {
    // We update our own screen directly from the HTTP response here, AND
    // this same change will also arrive a moment later via the WebSocket
    // broadcast (for every OTHER connected tab watching this incident).
    await runAction(() => updateIncidentStatus(incidentId, status));
  }

  async function handleAddTask(e: FormEvent) {
    e.preventDefault();
    if (!newTaskTitle.trim()) return;
    await createTask(incidentId, newTaskTitle.trim());
    // Notice we don't call setIncident here - we just let the WebSocket's
    // "task_created" event (triggered by our own request) update the list,
    // to avoid duplicating the "add this task locally" logic in two places.
    setNewTaskTitle("");
  }

  async function handlePostComment(e: FormEvent) {
    e.preventDefault();
    const body = newComment.trim();
    if (!body) return;

    // Clear the box immediately so it feels instant, then let the
    // "comment_created" broadcast put the message in the thread - the same
    // approach as adding a task above.
    setNewComment("");
    try {
      await postComment(incidentId, body);
      setActionError(null);
    } catch (err) {
      // Put the text back rather than losing what they typed.
      setNewComment(body);
      setActionError(err instanceof ApiError ? err.message : "Could not post that comment.");
    }
  }

  async function handleDeleteComment(comment: Comment) {
    try {
      await deleteComment(comment.id);
      setActionError(null);
    } catch (err) {
      setActionError(err instanceof ApiError ? err.message : "Could not delete that comment.");
    }
  }

  async function cycleTaskStatus(task: Task) {
    // Clicking a task's status icon cycles it forward: TODO -> IN_PROGRESS -> DONE -> TODO.
    const next: Record<Task["status"], Task["status"]> = {
      TODO: "IN_PROGRESS",
      IN_PROGRESS: "DONE",
      DONE: "TODO",
    };
    await updateTask(task.id, { status: next[task.status] });
  }

  async function assignTask(task: Task, userId: number) {
    await updateTask(task.id, { assigned_to: userId });
  }

  // Checked before the loading guard below, because once the incident is
  // deleted there is nothing left to load or display.
  if (deletedByOther) {
    return (
      <div className="page-body">
        <p>This incident was deleted by someone else.</p>
        <Link to="/incidents">← Back to incidents</Link>
      </div>
    );
  }

  if (loadError) {
    return (
      <div className="page-body">
        <div className="error-banner">{loadError}</div>
        <p>
          <Link to="/incidents">← Back to incidents</Link>
        </p>
      </div>
    );
  }

  if (!incident) return <div className="page-body">Loading...</div>;

  // Activity log entries can arrive in different orders depending on whether
  // they came from the initial fetch or a live WebSocket event, so we always
  // sort newest-first right before rendering rather than trusting insertion order.
  const sortedActivity = [...incident.activity_logs].sort(
    (a, b) => new Date(b.created_at).getTime() - new Date(a.created_at).getTime(),
  );

  // Comments read the other way round from the activity feed: oldest first,
  // like any conversation.
  const sortedComments = [...incident.comments].sort(
    (a, b) => new Date(a.created_at).getTime() - new Date(b.created_at).getTime(),
  );

  return (
    <div className="page">
      <header className="page-header">
        <Link to="/incidents" className="muted">
          ← Back to incidents
        </Link>

        {/* Who else has this page open right now. Shown up here because
            during an incident the first thing you want to know is whether
            anybody else has arrived yet. */}
        <div className="presence-bar" title={viewers.map((v) => v.name).join(", ")}>
          {viewers.length === 0 ? (
            <span className="muted">Connecting...</span>
          ) : (
            <>
              <span className="presence-dot" aria-hidden="true" />
              <span className="muted">
                {viewers.length === 1 ? "Just you" : `${viewers.length} people here`}
              </span>
              <span className="presence-names">{viewers.map((v) => v.name).join(", ")}</span>
            </>
          )}
        </div>
      </header>

      <div className="page-body incident-detail">
        <section className="card">
          <div className="incident-title-row">
            <h1>{incident.title}</h1>
            <div className="incident-title-actions">
              {/* Either you can join, or you're told that you already have.
                  A button that just disappeared once you joined would leave
                  no confirmation that anything happened - you couldn't tell
                  "I'm on this incident" from "the button hasn't loaded yet". */}
              {hasJoined ? (
                <span className="joined-label">✓ You've joined</span>
              ) : (
                <button onClick={handleJoin}>Join incident</button>
              )}
              {/* Only offered while nobody has taken it on - acknowledging
                  twice is a no-op on the backend, so the button would be a
                  lie rather than an action. */}
              {!incident.acknowledged_at && <button onClick={handleAcknowledge}>Acknowledge</button>}
              <button className="secondary" onClick={handleEscalate}>
                Escalate
              </button>
              {isCreator && (
                <button className="danger" onClick={handleDelete}>
                  Delete
                </button>
              )}
            </div>
          </div>

          {actionError && <div className="error-banner">{actionError}</div>}

          <p className="muted">{incident.description}</p>

          <div className="incident-row-badges">
            <SlaBadge incident={incident} now={now} />
            <SeverityBadge severity={incident.severity} />
            <StatusBadge status={incident.status} />
            {incident.department_name && <span className="badge badge-department">{incident.department_name}</span>}
          </div>

          {/* The Incident Commander: one named owner, because during a real
              incident "everyone is responsible" reliably means nobody is. */}
          <div className="commander-row">
            <span className="commander-label">Incident Commander</span>
            <select
              value={incident.commander_user_id ?? ""}
              onChange={(e) =>
                handleSetCommander(e.target.value === "" ? null : Number(e.target.value))
              }
              aria-label="Incident Commander"
            >
              <option value="">Nobody yet</option>
              {usersByDepartment.map(([departmentName, members]) => (
                <optgroup key={departmentName} label={departmentName}>
                  {members.map((u) => (
                    <option key={u.id} value={u.id}>
                      {u.job_title ? `${u.name} - ${u.job_title}` : u.name}
                    </option>
                  ))}
                </optgroup>
              ))}
            </select>
            {incident.commander && (
              <span className="muted">
                {incident.commander.name}
                {incident.commander.job_title ? ` · ${incident.commander.job_title}` : ""}
              </span>
            )}
          </div>

          <div className="status-actions">
            {STATUS_OPTIONS.map((status) => (
              <button
                key={status}
                className={status === incident.status ? "" : "secondary"}
                disabled={status === incident.status}
                onClick={() => handleStatusChange(status)}
              >
                {status}
              </button>
            ))}
          </div>
        </section>

        <div className="detail-columns">
          <section className="card">
            <h2>Tasks</h2>
            <ul className="task-list">
              {incident.tasks.map((task) => {
                const assignee = users.find((u) => u.id === task.assigned_to);
                return (
                  <li key={task.id} className="task-row">
                    <button className="task-status-btn" onClick={() => cycleTaskStatus(task)}>
                      <TaskStatusIcon status={task.status} />
                    </button>
                    <span className={task.status === "DONE" ? "task-title done" : "task-title"}>
                      {task.title}
                    </span>
                    <select
                      value={task.assigned_to ?? ""}
                      onChange={(e) => assignTask(task, Number(e.target.value))}
                    >
                      <option value="" disabled>
                        {assignee
                          ? `${assignee.name}${assignee.department ? ` · ${assignee.department.name}` : ""}`
                          : "Assign..."}
                      </option>
                      {/* One <optgroup> per department: the heading makes it
                          obvious who you're about to put on the incident. */}
                      {usersByDepartment.map(([departmentName, members]) => (
                        <optgroup key={departmentName} label={departmentName}>
                          {members.map((u) => (
                            <option key={u.id} value={u.id}>
                              {u.job_title ? `${u.name} - ${u.job_title}` : u.name}
                            </option>
                          ))}
                        </optgroup>
                      ))}
                    </select>
                  </li>
                );
              })}
              {incident.tasks.length === 0 && <p className="muted">No tasks yet.</p>}
            </ul>
            <form className="add-task-form" onSubmit={handleAddTask}>
              <input
                placeholder="Add a task..."
                value={newTaskTitle}
                onChange={(e) => setNewTaskTitle(e.target.value)}
              />
              <button type="submit">Add</button>
            </form>
          </section>

          {/* The conversation. Separate from Team Activity on the right:
              this is what PEOPLE said, that is what the APP recorded. */}
          <section className="card">
            <h2>Discussion</h2>
            <ul className="comment-list">
              {sortedComments.map((comment) => (
                <li key={comment.id} className="comment">
                  <div className="comment-head">
                    <span className="comment-author">{comment.user.name}</span>
                    <span className="comment-time muted">
                      {new Date(comment.created_at).toLocaleTimeString()}
                    </span>
                    {/* Only your own messages are yours to take back - the
                        backend enforces the same rule. */}
                    {comment.user.id === user?.id && (
                      <button
                        className="link-button"
                        onClick={() => handleDeleteComment(comment)}
                        aria-label="Delete comment"
                      >
                        delete
                      </button>
                    )}
                  </div>
                  <p className="comment-body">{comment.body}</p>
                </li>
              ))}
              {sortedComments.length === 0 && <p className="muted">No comments yet. Say something.</p>}
            </ul>
            <form className="add-comment-form" onSubmit={handlePostComment}>
              <textarea
                placeholder="Write a message..."
                value={newComment}
                rows={2}
                onChange={(e) => setNewComment(e.target.value)}
                onKeyDown={(e) => {
                  // Enter sends, Shift+Enter makes a new line - the
                  // convention in every chat tool, and worth matching when
                  // people are typing in a hurry.
                  if (e.key === "Enter" && !e.shiftKey) {
                    e.preventDefault();
                    handlePostComment(e);
                  }
                }}
              />
              <button type="submit">Send</button>
            </form>
          </section>

          <section className="card">
            <h2>Team Activity</h2>
            <ul className="activity-feed">
              {sortedActivity.map((log) => (
                <li key={log.id}>
                  <span className="activity-user">👤 {log.user.name}</span>
                  <span className="activity-message">{log.message}</span>
                  <span className="activity-time muted">
                    {new Date(log.created_at).toLocaleTimeString()}
                  </span>
                </li>
              ))}
              {incident.activity_logs.length === 0 && <p className="muted">No activity yet.</p>}
            </ul>
          </section>
        </div>
      </div>
    </div>
  );
}
