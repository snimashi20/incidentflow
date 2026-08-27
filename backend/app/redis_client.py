import redis.asyncio as redis

from app.config import settings

redis_client = redis.from_url(settings.redis_url, decode_responses=True)

INCIDENT_CHANNEL_PREFIX = "incident:"


def incident_channel(incident_id: int) -> str:
    return f"{INCIDENT_CHANNEL_PREFIX}{incident_id}"
