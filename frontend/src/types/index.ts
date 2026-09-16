// Shared TypeScript types describing the shapes of data our backend sends us.
// These mirror the C# DTOs in backend/IncidentFlow.Api/Dtos/ - keeping them
// in sync by hand is a common pattern in small full-stack apps (a larger app
// might auto-generate these from the OpenAPI spec ASP.NET Core can provide
// via Swagger/OpenAPI tooling instead).

// A union of string literals - TypeScript will only let a Severity variable
// hold one of these four exact strings, catching typos like "Hihg" at compile time.
export type Severity = "LOW" | "MEDIUM" | "HIGH" | "CRITICAL";
export type IncidentStatus = "OPEN" | "INVESTIGATING" | "RESOLVED";
export type TaskStatus = "TODO" | "IN_PROGRESS" | "DONE";

export interface User {
  id: number;
  email: string;
  name: string;
}

export interface Task {
  id: number;
  incident_id: number;
  title: string;
  status: TaskStatus;
  assigned_to: number | null;
  created_at: string; // ISO date string - JSON has no native date type, so timestamps travel as text
  updated_at: string;
}

export interface ActivityLogEntry {
  id: number;
  message: string;
  created_at: string;
  user: User;
}

// The short summary shown in the incidents list page.
export interface IncidentSummary {
  id: number;
  title: string;
  description: string;
  severity: Severity;
  status: IncidentStatus;
  created_by: number;
  created_at: string;
  updated_at: string;
}

// The full incident shown on the detail page, including everything nested
// inside it. `extends IncidentSummary` means it has all of IncidentSummary's
// fields plus these three extra ones.
export interface IncidentDetail extends IncidentSummary {
  tasks: Task[];
  activity_logs: ActivityLogEntry[];
  participants: User[];
}

// The shape of every message that arrives over the WebSocket - see
// useIncidentSocket.ts for where these get parsed and IncidentDetailPage.tsx
// for where each `type` gets handled.
export interface WsEvent<T = unknown> {
  type:
    | "incident_updated"
    | "task_created"
    | "task_updated"
    | "activity_created"
    | "user_joined";
  data: T;
}
