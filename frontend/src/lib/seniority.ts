// Everything the UI needs to know about the career ladder.
//
// The backend can compare seniority levels directly because C# enums carry
// a number behind each name (see Entities/Enums.cs). Over here a Seniority
// is just a string, so the ranking has to be written down explicitly - this
// array IS that ranking, lowest level first. Keep it in the same order as
// the C# enum.
import type { Seniority, User } from "../types";

export const SENIORITY_ORDER: Seniority[] = [
  "JUNIOR",
  "MID",
  "SENIOR",
  "STAFF",
  "LEAD",
  "MANAGER",
  "DIRECTOR",
  "VP",
  "EXECUTIVE",
];

// Short, human-friendly labels for the dropdowns - "Tech lead" reads better
// than the raw "LEAD" the API sends.
export const SENIORITY_LABELS: Record<Seniority, string> = {
  JUNIOR: "Junior",
  MID: "Mid-level",
  SENIOR: "Senior",
  STAFF: "Staff",
  LEAD: "Tech lead",
  MANAGER: "Manager",
  DIRECTOR: "Director",
  VP: "VP",
  EXECUTIVE: "Executive",
};

// A sortable number for a level: 0 for JUNIOR up to 8 for EXECUTIVE.
// Anyone whose level is missing (an activity-log entry only carries a name)
// sorts to the bottom rather than crashing.
export function seniorityRank(seniority?: Seniority | null): number {
  if (!seniority) return -1;
  return SENIORITY_ORDER.indexOf(seniority);
}

// LEAD and above: the people worth escalating a serious incident to.
export function isSenior(user: User): boolean {
  return seniorityRank(user.seniority) >= seniorityRank("LEAD");
}

// Most senior first, then alphabetically within the same level - the order
// you want when picking someone to own an incident.
export function bySeniorityThenName(a: User, b: User): number {
  const difference = seniorityRank(b.seniority) - seniorityRank(a.seniority);
  return difference !== 0 ? difference : a.name.localeCompare(b.name);
}
