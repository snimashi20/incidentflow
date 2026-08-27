import asyncio
import logging
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.config import settings
from app.database import init_db
from app.redis_client import redis_client
from app.routers import auth, incidents, tasks, users, ws
from app.ws_manager import redis_listener

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
logger = logging.getLogger("incidentflow")


@asynccontextmanager
async def lifespan(app: FastAPI):
    await init_db()
    listener_task = asyncio.create_task(redis_listener())
    logger.info("IncidentFlow backend started")
    yield
    listener_task.cancel()
    try:
        await listener_task
    except asyncio.CancelledError:
        pass
    await redis_client.aclose()
    logger.info("IncidentFlow backend stopped")


app = FastAPI(title="IncidentFlow", lifespan=lifespan)

app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.cors_origin_list,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

app.include_router(auth.router)
app.include_router(incidents.router)
app.include_router(tasks.router)
app.include_router(users.router)
app.include_router(ws.router)


@app.get("/api/health")
async def health() -> dict:
    return {"status": "ok"}
