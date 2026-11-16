// The main "dashboard" page: lists incidents, filters them, and lets you
// create a new one. This page does NOT use the WebSocket hook - it fetches
// the list when the filters change. Only the incident detail page
// (IncidentDetailPage.tsx) needs real-time updates, since that's the page
// multiple people actually watch together.
import { FormEvent, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { createIncident, deleteIncident, listIncidents } from "../api/incidents";
import { listDepartments } from "../api/departments";
import { ApiError } from "../api/client";
import { SeverityBadge, SlaBadge, StatusBadge } from "../components/Badges";
import { useNow } from "../lib/useNow";
import type { Department, IncidentStatus, IncidentSummary, Severity } from "../types";
import { useAuth } from "../context/AuthContext";

const STATUS_FILTERS: (IncidentStatus | "")[] = ["", "OPEN", "INVESTIGATING", "RESOLVED"];
const SEVERITY_FILTERS: (Severity | "")[] = ["", "CRITICAL", "HIGH", "MEDIUM", "LOW"];

// How long to wait after the last keystroke before searching. Without this,
// typing "checkout" would fire eight requests, and the answers could come
// back out of order - so the list would briefly show results for "checko".
const SEARCH_DEBOUNCE_MS = 300;

export function IncidentsListPage() {
  const { user, logout } = useAuth();
  const now = useNow(); // ticks so the SLA countdowns stay honest
  const [incidents, setIncidents] = useState<IncidentSummary[]>([]);
  const [departments, setDepartments] = useState<Department[]>([]);

  // ---- Filter state ----
  const [search, setSearch] = useState("");
  // The search box updates `search` on every keystroke, but only this
  // debounced copy is actually sent to the server (see the effect below).
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [statusFilter, setStatusFilter] = useState<IncidentStatus | "">("");
  const [severityFilter, setSeverityFilter] = useState<Severity | "">("");
  const [onlyMine, setOnlyMine] = useState(false);

  // ---- Create form state ----
  const [showForm, setShowForm] = useState(false);
  const [title, setTitle] = useState("");
  const [description, setDescription] = useState("");
  const [severity, setSeverity] = useState<Severity>("MEDIUM");
  // Which team owns the incident being created. Defaults to your own
  // department once it loads, since the thing you are reporting is usually
  // your team's problem - but it stays changeable, because sometimes it
  // isn't, and escalation follows whatever is picked here.
  const [departmentId, setDepartmentId] = useState<number | "">("");

  const [loading, setLoading] = useState(true);
  // Shown if a delete is rejected by the backend (e.g. 403 because the
  // incident isn't yours, or 404 because it's already gone).
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    listDepartments().then(setDepartments).catch(() => setDepartments([]));
  }, []);

  // Pre-select your own department in the create form once we know it.
  useEffect(() => {
    if (user?.department && departmentId === "") {
      setDepartmentId(user.department.id);
    }
  }, [user, departmentId]);

  // Wait for a pause in typing before letting the search term through.
  // The cleanup cancels the previous timer, so only the last keystroke in a
  // burst ever survives to trigger a fetch.
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedSearch(search), SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [search]);

  // Re-fetch whenever any filter changes. Listing every filter in the
  // dependency array is what makes this work: React re-runs the effect
  // whenever one of them is different from last render.
  useEffect(() => {
    let cancelled = false;
    setLoading(true);

    listIncidents({
      q: debouncedSearch,
      status: statusFilter || undefined,
      severity: severityFilter || undefined,
      mine: onlyMine || undefined,
    })
      .then((results) => {
        // Two fetches can be in flight at once (change a filter quickly
        // twice). Without this guard, a slow FIRST response could land after
        // a fast second one and overwrite the newer results with older ones.
        if (!cancelled) setIncidents(results);
      })
      .catch((err) => {
        if (!cancelled) setError(err instanceof ApiError ? err.message : "Could not load incidents.");
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [debouncedSearch, statusFilter, severityFilter, onlyMine]);

  async function handleCreate(e: FormEvent) {
    e.preventDefault();
    if (departmentId === "") return;

    try {
      const incident = await createIncident(title, description, severity, departmentId);
      // Add the new incident to the top of the list immediately, instead of
      // re-fetching the whole list from the server again.
      setIncidents((prev) => [incident, ...prev]);
      setTitle("");
      setDescription("");
      setSeverity("MEDIUM");
      setShowForm(false);
      setError(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not create that incident.");
    }
  }

  async function handleDelete(incident: IncidentSummary) {
    // Always confirm before something irreversible. From the list we don't
    // know how many tasks the incident has (the summary DTO doesn't include
    // them), so the warning stays general.
    const confirmed = window.confirm(
      `Delete "${incident.title}"?\n\n` +
        `Its tasks, comments and activity history go with it. This cannot be undone.`,
    );
    if (!confirmed) return;

    try {
      await deleteIncident(incident.id);
      // Drop it from the list we already have rather than re-fetching the
      // whole thing from the server - same approach as handleCreate above.
      setIncidents((prev) => prev.filter((i) => i.id !== incident.id));
      setError(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Could not delete that incident.");
    }
  }

  const filtersAreActive =
    debouncedSearch !== "" || statusFilter !== "" || severityFilter !== "" || onlyMine;

  function clearFilters() {
    setSearch("");
    setStatusFilter("");
    setSeverityFilter("");
    setOnlyMine(false);
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

        <div className="filter-bar">
          <input
            className="filter-search"
            placeholder="Search incidents..."
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            aria-label="Search incidents"
          />
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value as IncidentStatus | "")}
            aria-label="Filter by status"
          >
            {STATUS_FILTERS.map((option) => (
              <option key={option || "any"} value={option}>
                {option || "Any status"}
              </option>
            ))}
          </select>
          <select
            value={severityFilter}
            onChange={(e) => setSeverityFilter(e.target.value as Severity | "")}
            aria-label="Filter by severity"
          >
            {SEVERITY_FILTERS.map((option) => (
              <option key={option || "any"} value={option}>
                {option || "Any severity"}
              </option>
            ))}
          </select>
          <label className="filter-checkbox">
            <input type="checkbox" checked={onlyMine} onChange={(e) => setOnlyMine(e.target.checked)} />
            Only mine
          </label>
          {filtersAreActive && (
            <button className="secondary small" onClick={clearFilters}>
              Clear
            </button>
          )}
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
            <label>
              Department
              <select
                value={departmentId}
                onChange={(e) => setDepartmentId(Number(e.target.value))}
                required
              >
                <option value="" disabled>
                  Which team owns this?
                </option>
                {departments.map((department) => (
                  <option key={department.id} value={department.id}>
                    {department.name}
                  </option>
                ))}
              </select>
              {/* Says plainly what this choice costs later, since it is the
                  one field people would otherwise pick at random. */}
              <small className="muted">Escalation pulls in this department's senior people.</small>
            </label>
            <button type="submit">Create Incident</button>
          </form>
        )}

        {error && <div className="error-banner">{error}</div>}

        {loading && <p className="muted">Loading...</p>}

        <ul className="incident-list">
          {incidents.map((incident) => (
            // `key` here tells React which DOM element corresponds to which
            // incident across re-renders - required whenever you render a list.
            // The card itself is the <li>, with the link and the delete
            // button as siblings inside it. A <button> nested inside a link
            // is invalid HTML and makes clicks ambiguous (did you mean to
            // open it, or delete it?).
            <li key={incident.id} className="card incident-row">
              <Link to={`/incidents/${incident.id}`} className="incident-row-main">
                <strong>{incident.title}</strong>
                <p className="muted">{incident.description || "No description"}</p>
                {incident.department_name && (
                  <span className="incident-row-department muted">{incident.department_name}</span>
                )}
              </Link>
              <div className="incident-row-badges">
                <SlaBadge incident={incident} now={now} />
                <SeverityBadge severity={incident.severity} />
                <StatusBadge status={incident.status} />
                {/* Only the creator can delete, matching what the backend
                    allows - everyone else doesn't see the button at all. */}
                {incident.created_by === user?.id && (
                  <button
                    className="danger small"
                    onClick={() => handleDelete(incident)}
                    aria-label={`Delete ${incident.title}`}
                  >
                    Delete
                  </button>
                )}
              </div>
            </li>
          ))}
        </ul>

        {!loading && incidents.length === 0 && (
          <p className="muted">
            {filtersAreActive ? "No incidents match those filters." : "No incidents yet. Create one above."}
          </p>
        )}
      </div>
    </div>
  );
}
