// Small, reusable "badge" components for severity/status/task-status pills,
// so every page that needs to show e.g. a HIGH severity tag uses the exact
// same styling instead of repeating className logic everywhere.
import type { IncidentStatus, Severity, TaskStatus } from "../types";

// `Record<Severity, string>` means TypeScript will error if we ever forget
// to add a CSS class for a new Severity value, or add one that doesn't exist.
const SEVERITY_CLASS: Record<Severity, string> = {
  LOW: "badge badge-low",
  MEDIUM: "badge badge-medium",
  HIGH: "badge badge-high",
  CRITICAL: "badge badge-critical",
};

const STATUS_CLASS: Record<IncidentStatus, string> = {
  OPEN: "badge badge-open",
  INVESTIGATING: "badge badge-investigating",
  RESOLVED: "badge badge-resolved",
};

const TASK_STATUS_LABEL: Record<TaskStatus, string> = {
  TODO: "○",
  IN_PROGRESS: "◉",
  DONE: "☑",
};

export function SeverityBadge({ severity }: { severity: Severity }) {
  return <span className={SEVERITY_CLASS[severity]}>{severity}</span>;
}

export function StatusBadge({ status }: { status: IncidentStatus }) {
  return <span className={STATUS_CLASS[status]}>{status}</span>;
}

export function TaskStatusIcon({ status }: { status: TaskStatus }) {
  return <span className="task-status-icon">{TASK_STATUS_LABEL[status]}</span>;
}
