from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.deps import get_current_user
from app.models import ActivityLog, Incident, Task, User
from app.schemas import TaskCreate, TaskOut, TaskUpdate
from app.ws_manager import publish_event

router = APIRouter(tags=["tasks"])


async def _get_incident_or_404(db: AsyncSession, incident_id: int) -> Incident:
    incident = await db.get(Incident, incident_id)
    if incident is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Incident not found")
    return incident


async def _get_task_or_404(db: AsyncSession, task_id: int) -> Task:
    task = await db.get(Task, task_id)
    if task is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Task not found")
    return task


async def _log_activity(db: AsyncSession, incident_id: int, user: User, message: str) -> ActivityLog:
    log = ActivityLog(incident_id=incident_id, user_id=user.id, message=message)
    db.add(log)
    await db.flush()
    await db.refresh(log, attribute_names=["user"])
    return log


def _activity_payload(log: ActivityLog) -> dict:
    return {
        "id": log.id,
        "message": log.message,
        "created_at": log.created_at.isoformat(),
        "user": {"id": log.user.id, "email": log.user.email, "name": log.user.name},
    }


@router.post(
    "/api/incidents/{incident_id}/tasks", response_model=TaskOut, status_code=201
)
async def create_task(
    incident_id: int,
    payload: TaskCreate,
    db: AsyncSession = Depends(get_db),
    current_user: User = Depends(get_current_user),
) -> Task:
    await _get_incident_or_404(db, incident_id)

    task = Task(incident_id=incident_id, title=payload.title, assigned_to=payload.assigned_to)
    db.add(task)
    await db.flush()

    log = await _log_activity(db, incident_id, current_user, f"{current_user.name} added task \"{task.title}\"")
    await db.commit()
    await db.refresh(task)

    await publish_event(incident_id, "task_created", TaskOut.model_validate(task).model_dump(mode="json"))
    await publish_event(incident_id, "activity_created", _activity_payload(log))
    return task


@router.patch("/api/tasks/{task_id}", response_model=TaskOut)
async def update_task(
    task_id: int,
    payload: TaskUpdate,
    db: AsyncSession = Depends(get_db),
    current_user: User = Depends(get_current_user),
) -> Task:
    task = await _get_task_or_404(db, task_id)

    messages = []
    if payload.title is not None:
        task.title = payload.title
    if payload.status is not None and payload.status != task.status:
        messages.append(f"{current_user.name} changed task \"{task.title}\" status → {payload.status.value}")
        task.status = payload.status
    if payload.assigned_to is not None and payload.assigned_to != task.assigned_to:
        task.assigned_to = payload.assigned_to
        assignee = await db.get(User, payload.assigned_to)
        if assignee is not None:
            messages.append(f"{current_user.name} assigned \"{task.title}\" to {assignee.name}")

    await db.flush()

    logs = [await _log_activity(db, task.incident_id, current_user, m) for m in messages]
    await db.commit()
    await db.refresh(task)

    await publish_event(task.incident_id, "task_updated", TaskOut.model_validate(task).model_dump(mode="json"))
    for log in logs:
        await publish_event(task.incident_id, "activity_created", _activity_payload(log))
    return task
