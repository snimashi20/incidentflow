from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy.orm import selectinload

from app.database import get_db
from app.deps import get_current_user
from app.models import ActivityLog, Incident, IncidentParticipant, User
from app.schemas import IncidentCreate, IncidentDetail, IncidentOut, IncidentStatusUpdate, UserOut
from app.ws_manager import publish_event

router = APIRouter(prefix="/api/incidents", tags=["incidents"])


async def _log_activity(db: AsyncSession, incident_id: int, user: User, message: str) -> ActivityLog:
    log = ActivityLog(incident_id=incident_id, user_id=user.id, message=message)
    db.add(log)
    await db.flush()
    await db.refresh(log, attribute_names=["user"])
    return log


async def _get_incident_or_404(db: AsyncSession, incident_id: int) -> Incident:
    # populate_existing forces relationships to be reloaded even if this Incident
    # is already in the session's identity map with stale collections - which it
    # is, since every route re-fetches after mutating within the same request/session.
    result = await db.execute(
        select(Incident)
        .options(
            selectinload(Incident.tasks),
            selectinload(Incident.activity_logs).selectinload(ActivityLog.user),
            selectinload(Incident.participants).selectinload(IncidentParticipant.user),
        )
        .where(Incident.id == incident_id)
        .execution_options(populate_existing=True)
    )
    incident = result.scalar_one_or_none()
    if incident is None:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Incident not found")
    return incident


def _to_detail(incident: Incident) -> IncidentDetail:
    return IncidentDetail(
        id=incident.id,
        title=incident.title,
        description=incident.description,
        severity=incident.severity,
        status=incident.status,
        created_by=incident.created_by,
        created_at=incident.created_at,
        updated_at=incident.updated_at,
        tasks=list(incident.tasks),
        activity_logs=list(incident.activity_logs),
        participants=[p.user for p in incident.participants],
    )


@router.post("", response_model=IncidentDetail, status_code=status.HTTP_201_CREATED)
async def create_incident(
    payload: IncidentCreate,
    db: AsyncSession = Depends(get_db),
    current_user: User = Depends(get_current_user),
) -> IncidentDetail:
    incident = Incident(
        title=payload.title,
        description=payload.description,
        severity=payload.severity,
        created_by=current_user.id,
    )
    db.add(incident)
    await db.flush()

    db.add(IncidentParticipant(incident_id=incident.id, user_id=current_user.id))
    await _log_activity(db, incident.id, current_user, f"{current_user.name} created the incident")
    await db.commit()

    incident = await _get_incident_or_404(db, incident.id)
    return _to_detail(incident)


@router.get("", response_model=list[IncidentOut])
async def list_incidents(
    db: AsyncSession = Depends(get_db),
    current_user: User = Depends(get_current_user),
) -> list[Incident]:
    result = await db.execute(select(Incident).order_by(Incident.created_at.desc()))
    return list(result.scalars().all())


@router.get("/{incident_id}", response_model=IncidentDetail)
async def get_incident(
    incident_id: int,
    db: AsyncSession = Depends(get_db),
    current_user: User = Depends(get_current_user),
) -> IncidentDetail:
    incident = await _get_incident_or_404(db, incident_id)
    return _to_detail(incident)


@router.patch("/{incident_id}/status", response_model=IncidentDetail)
async def update_status(
    incident_id: int,
    payload: IncidentStatusUpdate,
    db: AsyncSession = Depends(get_db),
    current_user: User = Depends(get_current_user),
) -> IncidentDetail:
    incident = await _get_incident_or_404(db, incident_id)
    old_status = incident.status
    incident.status = payload.status
    await db.flush()

    log = await _log_activity(
        db, incident_id, current_user,
        f"{current_user.name} changed status {old_status.value} → {payload.status.value}",
    )
    await db.commit()

    incident = await _get_incident_or_404(db, incident_id)
    detail = _to_detail(incident)

    await publish_event(incident_id, "incident_updated", detail.model_dump(mode="json"))
    await publish_event(
        incident_id, "activity_created",
        {"id": log.id, "message": log.message, "created_at": log.created_at.isoformat(),
         "user": {"id": log.user.id, "email": log.user.email, "name": log.user.name}},
    )
    return detail


@router.post("/{incident_id}/join", response_model=IncidentDetail)
async def join_incident(
    incident_id: int,
    db: AsyncSession = Depends(get_db),
    current_user: User = Depends(get_current_user),
) -> IncidentDetail:
    incident = await _get_incident_or_404(db, incident_id)

    already_joined = any(p.user_id == current_user.id for p in incident.participants)
    if not already_joined:
        db.add(IncidentParticipant(incident_id=incident_id, user_id=current_user.id))
        log = await _log_activity(db, incident_id, current_user, f"{current_user.name} joined the incident")
        await db.commit()
        await publish_event(
            incident_id, "activity_created",
            {"id": log.id, "message": log.message, "created_at": log.created_at.isoformat(),
             "user": {"id": log.user.id, "email": log.user.email, "name": log.user.name}},
        )
        await publish_event(incident_id, "user_joined", {"user": UserOut.model_validate(current_user).model_dump()})

    incident = await _get_incident_or_404(db, incident_id)
    return _to_detail(incident)
