# IncidentFlow

A real-time incident and task management system designed to help teams track, manage, and collaborate during operational incidents.

## 📌 About the Project

IncidentFlow allows team members to create and manage operational incidents, assign tasks, track progress, and receive real-time updates.

For example, when a production issue occurs:

1. A team member creates an incident.
2. Tasks are created and assigned to team members.
3. Team members update task and incident statuses.
4. Connected users receive updates in real time without refreshing the application.

The project is being developed to explore real-time communication, caching and messaging, containerization, automated testing, and CI/CD.

---

## ✨ Planned Features

- Create and manage incidents
- Set incident severity and status
- Create and assign tasks
- Update task status
- Real-time incident updates
- Real-time activity feed
- User authentication
- Role-based access control
- Incident and task history

---

## 🚨 Responding to an incident

Opening an incident is the easy part. These are the things that make the rest
of it work like a real incident tool rather than a to-do list.

**Acknowledge, and the SLA clock.** Every incident gets a budget to be
*acknowledged* in, set by its severity: CRITICAL 15 minutes, HIGH 1 hour,
MEDIUM 4 hours, LOW 24 hours. Acknowledging is separate from moving the
status to INVESTIGATING on purpose - status describes the incident,
acknowledgement describes whether a human has turned up yet. A background
job (`Realtime/SlaMonitorBackgroundService.cs`) checks once a minute and
flags anything that blew its budget, which is the one thing in this app that
happens without somebody clicking a button. The budgets live in
`Common/SlaPolicy.cs`.

- `POST /api/incidents/{id}/acknowledge` - idempotent; the first responder
  keeps the credit.

**Escalation.** Every incident belongs to a department, and escalating pulls
in everyone in *that* department at tech-lead level or above, giving them
access to the incident. This is what the org chart is for: a failing
database escalates to the Cloud engineers, not to whoever happens to be the
most senior person in the building.

- `POST /api/incidents/{id}/escalate`

**Incident Commander.** One named person accountable for driving it to
resolution, because during a real incident "everyone is responsible"
reliably means nobody is. Naming someone also puts them on the incident.

- `PATCH /api/incidents/{id}/commander` with `{"user_id": 7}`, or
  `{"user_id": null}` to clear it.

**Discussion.** What people *said*, kept separate from the activity feed,
which is what the app *recorded*. There is no edit endpoint by design: a
timeline you can quietly rewrite afterwards is worth much less when you are
working out what actually happened.

- `POST /api/incidents/{id}/comments`, `DELETE /api/comments/{id}` (your own
  comments only).

**Who's here.** The incident page shows everyone with it open right now,
live over the same WebSocket. Note the limitation: presence is held in each
server process's memory, so behind multiple backend replicas each browser
only sees the people connected to the same one. Everything else in the app
survives replicas because it travels through Redis.

**Filtering the list.** `GET /api/incidents` takes `status`, `severity`,
`department_id`, `q` (free-text over title and description), `mine=true`,
and `page` / `page_size`.

---

## 🔐 Connecting a WebSocket

A browser cannot put an `Authorization` header on a WebSocket handshake, so
something has to travel in the URL - and a URL ends up in the server's access
log, in every proxy in between, and in the browser's own history. So the
thing in the URL is a **ticket**, not the access token:

1. `POST /api/auth/ws-ticket` with the normal `Authorization: Bearer <token>`
   header → `{"ticket": "...", "expires_in_seconds": 30}`
2. `GET /ws/incidents/{id}?ticket=<ticket>`

A ticket is random, lives 30 seconds, and works exactly once, so one that
leaks into a log file is already spent. `?token=<jwt>` is no longer accepted.

Two other things guarding the front door: `POST /api/auth/login` is rate
limited per IP address (see `Security/RateLimitPolicies.cs`), and new
passwords have a length floor (`Common/PasswordPolicy.cs`).

**Writes are atomic and announced afterwards.** Each endpoint stages its
changes - the incident, the participant rows, the activity lines - and saves
them in one go, so the feed can never describe a change that didn't happen.
WebSocket events are published only after the database has committed, and a
Redis outage is logged rather than thrown, so a failed broadcast never turns
a successful write into a 500. On Postgres, `Incident` and `TaskItem` use
`xmin` as a concurrency token: two people changing the same thing at once
gets the second one a `409 Conflict` instead of silently losing a change.

---

## 🏢 Demo company (seed data)

The backend seeds a fictional large tech company on startup, so the app has a
realistic org chart to work with instead of a handful of test accounts.

- **14 departments**, in three groups: product areas (Google Cloud, YouTube,
  Core Systems & Experiences, ...), corporate functions (Finance, Legal,
  People Operations, ...) and campus operations.
- **75 employees**, each belonging to **exactly one** department, with a job
  title, an employment type (FTE, or TVC for temps/vendors/contractors) and a
  seniority level.
- A full **career ladder**: `JUNIOR → MID → SENIOR → STAFF → LEAD → MANAGER →
  DIRECTOR → VP → EXECUTIVE`. Senior software engineers, tech leads,
  engineering managers, directors, VPs and two C-suite roles (CTO, CFO) are
  all seeded, and **every department has at least one person at LEAD or
  above**, so any incident has a senior owner to escalate to.

Why it matters: incidents belong to a department, and serious ones get
escalated. A production database failure is work for the backend engineers and
SREs in Cloud / Core Systems - the accountants in Finance are not responsible
for it and should never end up assigned to it. These are the queries that make
that possible:

- `GET /api/users?department_id=3` - only that department's people.
- `GET /api/users?department_id=3&min_seniority=LEAD` - only the people senior
  enough to escalate to (tech lead and above).
- `GET /api/departments/3/members` - the department's roster, most senior first.

The "assign task" dropdown uses the same idea: it groups people by department
and lists each group most-senior-first.

The data lives in `backend/IncidentFlow.Api/Data/SeedData.cs`; the (idempotent)
insert logic is in `DatabaseSeeder.cs`. Every demo account uses the password
`incidentflow123`, so you can log in at any level - try
`daniel.okonkwo@example.com` (Database Reliability Engineer, Cloud),
`evelyn.shaw@example.com` (Tech Lead, Cloud Databases) or
`sanjay.iyer@example.com` (CTO). Set `SEED_DEMO_EMPLOYEES=false` to skip
the demo people - departments are always seeded, since registration requires
picking one.

---

## 🛠 Tech Stack

### Frontend

- React
- TypeScript
- React Router

### Backend

- C#
- ASP.NET Core (Minimal APIs)
- REST APIs
- WebSockets

### Database

- PostgreSQL
- Entity Framework Core

### Real-Time & Caching

- Redis
- Redis Pub/Sub

### DevOps

- Docker
- Docker Compose
- GitHub Actions

### Testing

- xUnit

---

## 🏗 Planned Architecture

```text
                 ┌───────────────────┐
                 │   React + TS      │
                 └─────────┬─────────┘
                           │
                ┌──────────┴──────────┐
                │                     │
             REST API             WebSocket
                │                     │
                └──────────┬──────────┘
                           │
                    ┌──────▼──────┐
                    │ ASP.NET Core│
                    └───┬─────┬───┘
                        │     │
                        │     │
                ┌───────▼┐ ┌──▼────────┐
                │PostgreSQL│ │  Redis   │
                └────────┘ └───────────┘
```
