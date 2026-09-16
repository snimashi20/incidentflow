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
