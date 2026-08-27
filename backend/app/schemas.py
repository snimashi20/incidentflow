from datetime import datetime

from pydantic import BaseModel, ConfigDict, EmailStr

from app.models import Severity, IncidentStatus, TaskStatus


# ---- Auth ----

class UserCreate(BaseModel):
    email: EmailStr
    name: str
    password: str


class UserLogin(BaseModel):
    email: EmailStr
    password: str


class UserOut(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    email: EmailStr
    name: str


class Token(BaseModel):
    access_token: str
    token_type: str = "bearer"


# ---- Activity ----

class ActivityOut(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    message: str
    created_at: datetime
    user: UserOut


# ---- Tasks ----

class TaskCreate(BaseModel):
    title: str
    assigned_to: int | None = None


class TaskUpdate(BaseModel):
    title: str | None = None
    status: TaskStatus | None = None
    assigned_to: int | None = None


class TaskOut(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    incident_id: int
    title: str
    status: TaskStatus
    assigned_to: int | None
    created_at: datetime
    updated_at: datetime


# ---- Incidents ----

class IncidentCreate(BaseModel):
    title: str
    description: str = ""
    severity: Severity


class IncidentStatusUpdate(BaseModel):
    status: IncidentStatus


class IncidentOut(BaseModel):
    model_config = ConfigDict(from_attributes=True)

    id: int
    title: str
    description: str
    severity: Severity
    status: IncidentStatus
    created_by: int
    created_at: datetime
    updated_at: datetime


class IncidentDetail(IncidentOut):
    tasks: list[TaskOut] = []
    activity_logs: list[ActivityOut] = []
    participants: list[UserOut] = []
