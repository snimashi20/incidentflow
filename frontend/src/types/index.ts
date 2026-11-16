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

// Which broad group a department belongs to - used to show the register
// form's dropdown in sections instead of as one long list of 14.
export type DepartmentKind = "PRODUCT_AREA" | "CORPORATE_FUNCTION" | "CAMPUS_OPERATIONS";

// FTE = full-time employee, TVC = temp / vendor / contractor.
export type EmploymentType = "FTE" | "TVC";

// The career ladder, matching the Seniority enum in
// backend/IncidentFlow.Api/Entities/Enums.cs. JUNIOR..STAFF are individual
// contributors, LEAD is a hands-on tech lead, and MANAGER upwards is
// management - which is who an incident gets escalated to. The order lives
// in lib/seniority.ts, since a TypeScript union is just a set of strings
// and carries no ranking by itself.
export type Seniority =
  | "JUNIOR"
  | "MID"
  | "SENIOR"
  | "STAFF"
  | "LEAD"
  | "MANAGER"
  | "DIRECTOR"
  | "VP"
  | "EXECUTIVE";

export interface Department {
  id: number;
  code: string; // short stable identifier, e.g. "CLOUD"
  name: string; // display name, e.g. "Google Cloud"
  kind: DepartmentKind;
  description: string;
  // Only sent by GET /api/departments - when a department is nested inside
  // a user the backend leaves it out rather than running an extra count.
  member_count?: number | null;
}

export interface User {
  id: number;
  email: string;
  name: string;
  // These three are only filled in by the endpoints that load the whole
  // employee record (/api/users, /api/auth/me, /api/departments/:id/members).
  // Somewhere like an activity log entry, which only ever shows a name, the
  // backend sends null instead of doing the extra work.
  job_title?: string | null;
  seniority?: Seniority | null;
  employment_type?: EmploymentType | null;
  department?: Department | null;
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

// One message a person typed into the incident thread. Separate from
// ActivityLogEntry above, which is what the APP recorded - see
// backend/IncidentFlow.Api/Entities/Comment.cs for why the two are apart.
export interface Comment {
  id: number;
  incident_id: number;
  body: string;
  created_at: string;
  user: User;
}

// Somebody with this incident's page open right now. Sent by the
// "presence_updated" WebSocket event - this is live, not stored anywhere,
// so it vanishes the moment they close the tab.
export interface Viewer {
  user_id: number;
  email: string;
  name: string;
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
  // The team that owns this incident, and therefore the people escalation
  // reaches for.
  department_id: number;
  department_name: string | null;
  // The one person accountable for driving it to resolution. Null until
  // somebody takes the role.
  commander_user_id: number | null;
  // When a human first took it on. Null means nobody has yet - which is
  // what the SLA clock below is counting against.
  acknowledged_at: string | null;
  escalated_at: string | null;
  resolved_at: string | null;
  // When acknowledgement is due, computed by the server from the severity.
  // The countdown on screen just compares this to the current time, so the
  // SLA rules themselves live in exactly one place: the backend's
  // Common/SlaPolicy.cs.
  sla_deadline: string;
  // Stamped by the backend's background monitor once the deadline passes
  // with nobody having acknowledged. Stays set afterwards, so a late
  // response is still visible in the record once somebody does turn up.
  sla_breached_at: string | null;
}

// The full incident shown on the detail page, including everything nested
// inside it. `extends IncidentSummary` means it has all of IncidentSummary's
// fields plus these extra ones.
export interface IncidentDetail extends IncidentSummary {
  tasks: Task[];
  activity_logs: ActivityLogEntry[];
  participants: User[];
  comments: Comment[];
  commander: User | null;
}

// The shape of every message that arrives over the WebSocket - see
// useIncidentSocket.ts for where these get parsed and IncidentDetailPage.tsx
// for where each `type` gets handled.
export interface WsEvent<T = unknown> {
  type:
    | "incident_updated"
    // Sent when someone deletes the incident you're looking at. Its `data`
    // is just `{ id }` - there's no incident left to describe.
    | "incident_deleted"
    | "task_created"
    | "task_updated"
    | "activity_created"
    | "user_joined"
    // Somebody posted or removed a message in the thread.
    | "comment_created"
    // `data` is `{ id, incident_id }` - the message is gone, so there is
    // nothing left to send but its id.
    | "comment_deleted"
    // Who currently has this page open. Sent whenever anyone connects or
    // disconnects, and once to every new connection the moment it opens.
    // `data` is `{ viewers: Viewer[] }`.
    | "presence_updated"
    // Sent by the backend's background SLA monitor, NOT by a person - this
    // is the one event in the app nobody clicked a button to cause.
    | "sla_breached";
  data: T;
}
