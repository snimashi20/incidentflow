// Thin wrappers around the incident/task/user backend endpoints - one small
// typed function per API call, used by the page components.
import { api } from "./client";
import type { IncidentDetail, IncidentStatus, IncidentSummary, Severity, Task, User } from "../types";

export function listIncidents(): Promise<IncidentSummary[]> {
  return api.get<IncidentSummary[]>("/api/incidents");
}

export function getIncident(id: number): Promise<IncidentDetail> {
  return api.get<IncidentDetail>(`/api/incidents/${id}`);
}

export function createIncident(title: string, description: string, severity: Severity): Promise<IncidentDetail> {
  return api.post<IncidentDetail>("/api/incidents", { title, description, severity });
}

export function updateIncidentStatus(id: number, status: IncidentStatus): Promise<IncidentDetail> {
  return api.patch<IncidentDetail>(`/api/incidents/${id}/status`, { status });
}

export function joinIncident(id: number): Promise<IncidentDetail> {
  return api.post<IncidentDetail>(`/api/incidents/${id}/join`);
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
