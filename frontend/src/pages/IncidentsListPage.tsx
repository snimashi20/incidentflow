import { FormEvent, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { createIncident, listIncidents } from "../api/incidents";
import { SeverityBadge, StatusBadge } from "../components/Badges";
import type { IncidentSummary, Severity } from "../types";
import { useAuth } from "../context/AuthContext";

export function IncidentsListPage() {
  const { user, logout } = useAuth();
  const [incidents, setIncidents] = useState<IncidentSummary[]>([]);
  const [showForm, setShowForm] = useState(false);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [severity, setSeverity] = useState<Severity>("MEDIUM");
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    listIncidents()
      .then(setIncidents)
      .finally(() => setLoading(false));
  }, []);

  async function handleCreate(e: FormEvent) {
    e.preventDefault();
    const incident = await createIncident(title, description, severity);
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
