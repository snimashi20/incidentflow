// Wrappers around the /api/departments/* endpoints - the company org chart.
import { api } from "./client";
import type { Department, User } from "../types";

// Used by the registration form, so this one has to work without a token
// (the backend allows it unauthenticated for exactly that reason).
export function listDepartments(): Promise<Department[]> {
  return api.get<Department[]>("/api/departments");
}

// Everyone in one department: "who could actually work on this?". A failed
// database is a job for the engineers in Cloud, not for Finance.
export function listDepartmentMembers(departmentId: number): Promise<User[]> {
  return api.get<User[]>(`/api/departments/${departmentId}/members`);
}
