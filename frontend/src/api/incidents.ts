// Thin wrappers around the incident/task/user backend endpoints - one small
// typed function per API call, used by the page components.
import { api } from "./client";
import type { IncidentDetail, IncidentStatus, IncidentSummary, Severity, Task, User } from "../types";

// Every filter is optional - passing none of them gives the plain list.
// They match the query parameters the backend reads in
// Endpoints/IncidentEndpoints.cs.
export interface IncidentFilters {
  status?: IncidentStatus;
  severity?: Severity;
  departmentId?: number;
  // Free-text search across title and description.
  q?: string;
  // Only incidents you are personally on. For a manager or above - who can
  // otherwise see everything - this is the switch back to just their own work.
  mine?: boolean;
  page?: number;
  pageSize?: number;
}

export function listIncidents(filters: IncidentFilters = {}): Promise<IncidentSummary[]> {
  // URLSearchParams builds the "?status=OPEN&q=checkout" part for us, with
  // the escaping handled - safer than gluing strings together, which breaks
  // the moment someone searches for a word with a space or an "&" in it.
  const params = new URLSearchParams();
  if (filters.status) params.set("status", filters.status);
  if (filters.severity) params.set("severity", filters.severity);
  if (filters.departmentId) params.set("department_id", String(filters.departmentId));
  if (filters.q?.trim()) params.set("q", filters.q.trim());
  if (filters.mine) params.set("mine", "true");
  if (filters.page) params.set("page", String(filters.page));
  if (filters.pageSize) params.set("page_size", String(filters.pageSize));

  const query = params.toString();
  return api.get<IncidentSummary[]>(`/api/incidents${query ? `?${query}` : ""}`);
}

export function getIncident(id: number): Promise<IncidentDetail> {
  return api.get<IncidentDetail>(`/api/incidents/${id}`);
}

export function createIncident(
  title: string,
  description: string,
  severity: Severity,
  departmentId: number,
): Promise<IncidentDetail> {
  // department_id is required by the backend: an incident that belongs to no
  // team cannot be escalated to anyone.
  return api.post<IncidentDetail>("/api/incidents", {
    title,
    description,
    severity,
    department_id: departmentId,
  });
}

// "I have seen this and I am on it." Deliberately separate from moving the
// status to INVESTIGATING - the gap between when an incident was opened and
// when somebody acknowledged it is the number that says whether your
// alerting actually reaches people.
export function acknowledgeIncident(id: number): Promise<IncidentDetail> {
  return api.post<IncidentDetail>(`/api/incidents/${id}/acknowledge`);
}

// Pulls in everyone at tech-lead level or above in the incident's own
// department, and gives them access to it.
export function escalateIncident(id: number): Promise<IncidentDetail> {
  return api.post<IncidentDetail>(`/api/incidents/${id}/escalate`);
}

// Names the Incident Commander, or clears the role by passing null.
export function setIncidentCommander(id: number, userId: number | null): Promise<IncidentDetail> {
  return api.patch<IncidentDetail>(`/api/incidents/${id}/commander`, { user_id: userId });
}

export function updateIncidentStatus(id: number, status: IncidentStatus): Promise<IncidentDetail> {
  return api.patch<IncidentDetail>(`/api/incidents/${id}/status`, { status });
}

export function joinIncident(id: number): Promise<IncidentDetail> {
  return api.post<IncidentDetail>(`/api/incidents/${id}/join`);
}

// Permanently deletes an incident along with its tasks and activity history.
// The backend only allows this for the user who created the incident, so
// this can reject with an ApiError of status 403 - callers should be ready
// to show that message rather than assume it always succeeds.
export function deleteIncident(id: number): Promise<void> {
  return api.del(`/api/incidents/${id}`);
}

export function createTask(incidentId: number, title: string, assignedTo?: number): Promise<Task> {
  return api.post<Task>(`/api/incidents/${incidentId}/tasks`, { title, assigned_to: assignedTo ?? null });
}

export function updateTask(
  taskId: number,
  // A "partial update" object - only the fields you actually want to change
  // need to be included, matching the backend's TaskUpdate schema.
  changes: { title?: string; status?: Task["status"]; assigned_to?: number },
): Promise<Task> {
  return api.patch<Task>(`/api/tasks/${taskId}`, changes);
}

export function listUsers(): Promise<User[]> {
  // Used to populate the "assign task to..." dropdown.
  return api.get<User[]>("/api/users");
}
