// The main "dashboard" page: lists all incidents and lets you create a new
// one. This page does NOT use the WebSocket hook - it only fetches the list
// once on load. Only the incident detail page (IncidentDetailPage.tsx) needs
// real-time updates, since that's the page multiple people actually watch together.
import { FormEvent, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { createIncident, listIncidents } from "../api/incidents";
import { SeverityBadge, StatusBadge } from "../components/Badges";
import type { IncidentSummary, Severity } from "../types";
import { useAuth } from "../context/AuthContext";

export function IncidentsListPage() {
  const { user, logout } = useAuth();
  const [incidents, setIncidents] = useState<IncidentSummary[]>([]);
  const [showForm, setShowForm] = useState(false); // toggles the "create incident" form open/closed
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [severity, setSeverity] = useState<Severity>("MEDIUM");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    // Empty dependency array [] means this only runs once, right after the
    // component first appears on screen - fetch the incident list from the API.
    listIncidents()
      .then(setIncidents)
      .finally(() => setLoading(false));
  }, []);

  async function handleCreate(e: FormEvent) {
    e.preventDefault();
    const incident = await createIncident(title, description, severity);
    // Add the new incident to the top of the list immediately, instead of
    // re-fetching the whole list from the server again.
    setIncidents((prev) => [incident, ...prev]);
    setTitle("");
    setDescription("");
    setSeverity("MEDIUM");
    setShowForm(false);
  }

  return (
    <div className="page">
      <header className="page-header">
        <h1>IncidentFlow</h1>
        <div className="header-actions">
          <span className="muted">{user?.name}</span>
          <button onClick={logout} className="secondary">
            Log out
          </button>
        </div>
      </header>

      <div className="page-body">
        <div className="list-toolbar">
          <h2>Incidents</h2>
          <button onClick={() => setShowForm((v) => !v)}>{showForm ? "Cancel" : "+ New Incident"}</button>
        </div>

        {showForm && (
          <form className="card create-form" onSubmit={handleCreate}>
            <label>
              Title
              <input value={title} onChange={(e) => setTitle(e.target.value)} required />
            </label>
            <label>
              Description
              <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={2} />
            </label>
            <label>
              Severity
              <select value={severity} onChange={(e) => setSeverity(e.target.value as Severity)}>
                <option value="LOW">Low</option>
                <option value="MEDIUM">Medium</option>
                <option value="HIGH">High</option>
                <option value="CRITICAL">Critical</option>
              </select>
            </label>
            <button type="submit">Create Incident</button>
          </form>
        )}

        {loading && <p className="muted">Loading...</p>}

        <ul className="incident-list">
          {incidents.map((incident) => (
            // `key` here tells React which DOM element corresponds to which
            // incident across re-renders - required whenever you render a list.
            <li key={incident.id}>
              <Link to={`/incidents/${incident.id}`} className="card incident-row">
                <div>
                  <strong>{incident.title}</strong>
                  <p className="muted">{incident.description || "No description"}</p>
                </div>
                <div className="incident-row-badges">
                  <SeverityBadge severity={incident.severity} />
                  <StatusBadge status={incident.status} />
                </div>
              </Link>
            </li>
          ))}
        </ul>

        {!loading && incidents.length === 0 && <p className="muted">No incidents yet. Create one above.</p>}
      </div>
    </div>
  );
}
