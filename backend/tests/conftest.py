from collections.abc import AsyncGenerator

import pytest_asyncio
from httpx import ASGITransport, AsyncClient
from sqlalchemy.ext.asyncio import async_sessionmaker, create_async_engine
from sqlalchemy.pool import StaticPool

from app import main as main_module
from app.database import Base, get_db
from app.main import app
from app.routers import incidents as incidents_module
from app.routers import tasks as tasks_module
from app.ws_manager import manager

TEST_DATABASE_URL = "sqlite+aiosqlite:///:memory:"

test_engine = create_async_engine(
    TEST_DATABASE_URL, connect_args={"check_same_thread": False}, poolclass=StaticPool
)
TestSessionLocal = async_sessionmaker(test_engine, expire_on_commit=False)


async def _override_get_db() -> AsyncGenerator:
    async with TestSessionLocal() as session:
        yield session


async def _fake_publish_event(incident_id: int, event_type: str, data: dict) -> None:
    """Skip the real Redis hop in tests: broadcast straight to local sockets.

    This tests our own connection-manager/broadcast logic without requiring a
    live Redis server. The pub/sub wiring itself has no branching logic worth
    testing beyond "it forwards what it's given".
    """
    await manager.broadcast_local(incident_id, {"type": event_type, "data": data})


async def _noop() -> None:
    return None


app.dependency_overrides[get_db] = _override_get_db


@pytest_asyncio.fixture(autouse=True)
async def _setup(monkeypatch):
    monkeypatch.setattr(incidents_module, "publish_event", _fake_publish_event)
    monkeypatch.setattr(tasks_module, "publish_event", _fake_publish_event)
    monkeypatch.setattr(main_module, "init_db", _noop)
    monkeypatch.setattr(main_module, "redis_listener", _noop)

    async with test_engine.begin() as conn:
        await conn.run_sync(Base.metadata.create_all)
    yield
    async with test_engine.begin() as conn:
        await conn.run_sync(Base.metadata.drop_all)


@pytest_asyncio.fixture
async def client() -> AsyncGenerator[AsyncClient, None]:
    transport = ASGITransport(app=app)
    async with AsyncClient(transport=transport, base_url="http://test") as ac:
        yield ac


@pytest_asyncio.fixture
async def register_and_login(client: AsyncClient):
    async def _make(email: str, name: str = "Test User", password: str = "supersecret123") -> dict:
        await client.post("/api/auth/register", json={"email": email, "name": name, "password": password})
        resp = await client.post("/api/auth/login", json={"email": email, "password": password})
        token = resp.json()["access_token"]
        return {"Authorization": f"Bearer {token}"}

    return _make
