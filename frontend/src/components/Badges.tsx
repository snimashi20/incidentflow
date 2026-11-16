// Small, reusable "badge" components for severity/status/task-status pills,
// so every page that needs to show e.g. a HIGH severity tag uses the exact
// same styling instead of repeating className logic everywhere.
import type { IncidentStatus, IncidentSummary, Severity, TaskStatus } from "../types";
import { slaLabel, slaState } from "../lib/sla";

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

// Each status also gets its own CSS class, so index.css can color the icon
// differently per status (grey to-do, green in-progress, grey done) without
// this component needing to know anything about colors.
const TASK_STATUS_CLASS: Record<TaskStatus, string> = {
  TODO: "task-status-icon task-status-todo",
  IN_PROGRESS: "task-status-icon task-status-in-progress",
  DONE: "task-status-icon task-status-done",
};

export function SeverityBadge({ severity }: { severity: Severity }) {
  return <span className={SEVERITY_CLASS[severity]}>{severity}</span>;
}

export function StatusBadge({ status }: { status: IncidentStatus }) {
  return <span className={STATUS_CLASS[status]}>{status}</span>;
}

export function TaskStatusIcon({ status }: { status: TaskStatus }) {
  return <span className={TASK_STATUS_CLASS[status]}>{TASK_STATUS_LABEL[status]}</span>;
}

// The acknowledgement clock. `now` is passed in rather than read here, so
// the countdown re-renders when the page's clock ticks (see lib/useNow.ts) -
// a component that called new Date() itself would render once and then
// quietly stop being true.
export function SlaBadge({ incident, now }: { incident: IncidentSummary; now: Date }) {
  const state = slaState(incident, now);

  // Nothing useful to say about the acknowledgement clock on an incident
  // that is already closed, so don't add a badge just to fill the space.
  if (state === "closed") return null;

  const className =
    state === "breached"
      ? "badge badge-sla-breached"
      : state === "met"
        ? "badge badge-sla-met"
        : "badge badge-sla-counting";

  return (
    <span className={className} title="Time to acknowledge, set by the incident's severity">
      {state === "breached" ? "⚠ " : state === "met" ? "✓ " : "⏱ "}
      {slaLabel(incident, now)}
    </span>
  );
}
