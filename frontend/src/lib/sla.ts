// Turning the backend's SLA timestamps into something readable on screen.
//
// Note what this file does NOT contain: the SLA rules themselves. How many
// minutes a CRITICAL incident gets is decided once, on the server, in
// Common/SlaPolicy.cs, and arrives here already worked out as the
// `sla_deadline` field. Copying the minutes-per-severity table into the
// frontend would mean two places to change it and one of them silently
// going stale.
import type { IncidentSummary } from "../types";

// What the clock is doing for one incident. Four states, because the UI
// wants to say something different in each:
//   "met"      - somebody acknowledged it in time. Nothing to chase.
//   "breached" - the deadline passed with nobody acknowledging.
//   "counting" - still unacknowledged, still within budget.
//   "closed"   - resolved, so the acknowledgement clock is moot.
export type SlaState = "met" | "breached" | "counting" | "closed";

export function slaState(incident: IncidentSummary, now: Date = new Date()): SlaState {
  // A breach is recorded on the incident rather than recalculated, so it
  // stays visible even after somebody finally acknowledges - checking it
  // first is what makes "acknowledged late" readable rather than looking
  // identical to "acknowledged on time".
  if (incident.sla_breached_at) return "breached";
  if (incident.acknowledged_at) return "met";
  if (incident.status === "RESOLVED") return "closed";

  // The backend's background monitor only runs once a minute, so between
  // ticks an incident can be past its deadline without being stamped yet.
  // Treating that as breached on screen keeps the page honest rather than
  // showing "0 minutes left" for up to a minute.
  return new Date(incident.sla_deadline) <= now ? "breached" : "counting";
}

// "4m" / "2h 10m" / "3d". Deliberately rough: when you are watching a
// countdown during an outage, the seconds are noise.
export function formatRemaining(deadline: string, now: Date = new Date()): string {
  const milliseconds = Math.abs(new Date(deadline).getTime() - now.getTime());
  const minutes = Math.floor(milliseconds / 60000);

  if (minutes < 1) return "<1m";
  if (minutes < 60) return `${minutes}m`;

  const hours = Math.floor(minutes / 60);
  if (hours < 24) {
    const leftoverMinutes = minutes % 60;
    return leftoverMinutes === 0 ? `${hours}h` : `${hours}h ${leftoverMinutes}m`;
  }

  return `${Math.floor(hours / 24)}d`;
}

// The short text that goes on the badge.
export function slaLabel(incident: IncidentSummary, now: Date = new Date()): string {
  switch (slaState(incident, now)) {
    case "breached":
      return `SLA breached by ${formatRemaining(incident.sla_deadline, now)}`;
    case "met":
      return "Acknowledged";
    case "counting":
      return `${formatRemaining(incident.sla_deadline, now)} to acknowledge`;
    case "closed":
      return "Resolved";
  }
}
