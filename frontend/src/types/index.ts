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
  created_at: string;
  updated_at: string;
}

export interface ActivityLogEntry {
  id: number;
  message: string;
  created_at: string;
  user: User;
}

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

export interface IncidentDetail extends IncidentSummary {
  tasks: Task[];
  activity_logs: ActivityLogEntry[];
  participants: User[];
}

export interface WsEvent<T = unknown> {
  type:
    | "incident_updated"
    | "task_created"
    | "task_updated"
    | "activity_created"
    | "user_joined";
  data: T;
}
