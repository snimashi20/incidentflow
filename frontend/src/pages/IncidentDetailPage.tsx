// This is the most important page in the app - it's the one that actually
// shows the real-time behaviour (see the screenshot in the project brief:
// status, tasks, and team activity all updating live). It combines:
//   1. A normal initial fetch (getIncident) to load the page the first time.
//   2. A WebSocket subscription (useIncidentSocket) that keeps the page in
//      sync afterwards, whenever ANYONE (including other browser tabs/users)
//      changes something about this incident.
import { FormEvent, useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import {
  createTask,
  getIncident,
  joinIncident,
  listUsers,
  updateIncidentStatus,
  updateTask,
} from "../api/incidents";
import { SeverityBadge, StatusBadge, TaskStatusIcon } from "../components/Badges";
import { useIncidentSocket } from "../hooks/useIncidentSocket";
import { useAuth } from "../context/AuthContext";
import type {
  ActivityLogEntry,
  IncidentDetail as IncidentDetailType,
  IncidentStatus,
  Task,
  User,
  WsEvent,
} from "../types";

const STATUS_OPTIONS: IncidentStatus[] = ["OPEN", "INVESTIGATING", "RESOLVED"];

export function IncidentDetailPage() {
  // useParams reads the `:id` part of the URL (e.g. /incidents/1 -> id = "1"),
  // as defined in App.tsx's route configuration.
  const { id } = useParams<{ id: string }>();
  const incidentId = Number(id);
  const { user } = useAuth();

  const [incident, setIncident] = useState<IncidentDetailType | null>(null);
  const [users, setUsers] = useState<User[]>([]);
  const [newTaskTitle, setNewTaskTitle] = useState("");

  useEffect(() => {
    // Load this incident's full detail, and the list of users (for the
    // "assign task" dropdown), once when the page opens.
    getIncident(incidentId).then(setIncident);
    listUsers().then(setUsers);
  }, [incidentId]);

  // useMemo recalculates this only when `incident` or `user` actually change,
  // instead of on every single render - here it's cheap either way, but it's
  // a common React pattern worth knowing.
  const hasJoined = useMemo(
    () => incident?.participants.some((p) => p.id === user?.id) ?? false,
    [incident, user],
  );

  // This is where the live updates actually happen. useIncidentSocket opens
  // a WebSocket and calls this function every time the backend broadcasts an
  // event for this incident (see ws_manager.py + routers/incidents.py/tasks.py
  // on the backend for where these events get published).
  useIncidentSocket(incidentId, (event: WsEvent) => {
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
        default:
          return prev;
      }
    });
  });

  async function handleJoin() {
    const updated = await joinIncident(incidentId);
    setIncident(updated);
  }

  async function handleStatusChange(status: IncidentStatus) {
    const updated = await updateIncidentStatus(incidentId, status);
    // We update our own screen directly from the HTTP response here, AND
    // this same change will also arrive a moment later via the WebSocket
    // broadcast (for every OTHER connected tab watching this incident).
    setIncident(updated);
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

  if (!incident) return <div className="page-body">Loading...</div>;

  // Activity log entries can arrive in different orders depending on whether
  // they came from the initial fetch or a live WebSocket event, so we always
  // sort newest-first right before rendering rather than trusting insertion order.
  const sortedActivity = [...incident.activity_logs].sort(
    (a, b) => new Date(b.created_at).getTime() - new Date(a.created_at).getTime(),
  );

  return (
    <div className="page">
      <header className="page-header">
        <Link to="/incidents" className="muted">
          ← Back to incidents
        </Link>
      </header>

      <div className="page-body incident-detail">
        <section className="card">
          <div className="incident-title-row">
            <h1>{incident.title}</h1>
            {!hasJoined && <button onClick={handleJoin}>Join incident</button>}
          </div>
          <p className="muted">{incident.description}</p>
          <div className="incident-row-badges">
            <SeverityBadge severity={incident.severity} />
            <StatusBadge status={incident.status} />
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
                        {assignee ? assignee.name : "Assign..."}
                      </option>
                      {users.map((u) => (
                        <option key={u.id} value={u.id}>
                          {u.name}
                        </option>
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
