import asyncio
import json
import logging

from fastapi import WebSocket

from app.redis_client import INCIDENT_CHANNEL_PREFIX, incident_channel, redis_client

logger = logging.getLogger("incidentflow.ws")


class ConnectionManager:
    """Tracks WebSocket connections held by *this* server process, keyed by incident id.

    Broadcasting never writes to these sockets directly from request handlers -
    every event is published to Redis instead (see publish_event below), and the
    single pub/sub listener below fans it out locally. That indirection is what
    lets this run behind multiple backend replicas without incidents' events
    getting split across processes.
    """

    def __init__(self) -> None:
        self._connections: dict[int, set[WebSocket]] = {}

    async def connect(self, incident_id: int, websocket: WebSocket) -> None:
        await websocket.accept()
        self._connections.setdefault(incident_id, set()).add(websocket)

    def disconnect(self, incident_id: int, websocket: WebSocket) -> None:
        connections = self._connections.get(incident_id)
        if connections and websocket in connections:
            connections.remove(websocket)
            if not connections:
                self._connections.pop(incident_id, None)

    async def broadcast_local(self, incident_id: int, message: dict) -> None:
        connections = self._connections.get(incident_id)
        if not connections:
            return
        payload = json.dumps(message)
        dead: list[WebSocket] = []
        for ws in connections:
            try:
                await ws.send_text(payload)
            except Exception:
                dead.append(ws)
        for ws in dead:
            self.disconnect(incident_id, ws)


manager = ConnectionManager()


async def publish_event(incident_id: int, event_type: str, data: dict) -> None:
    message = {"type": event_type, "data": data}
    await redis_client.publish(incident_channel(incident_id), json.dumps(message))


async def redis_listener() -> None:
    """Background task: subscribes once to every incident channel and re-broadcasts
    each message to whichever locally-connected clients belong to that incident."""
    pubsub = redis_client.pubsub()
    await pubsub.psubscribe(f"{INCIDENT_CHANNEL_PREFIX}*")
    try:
        async for message in pubsub.listen():
            if message["type"] != "pmessage":
                continue
            try:
                channel: str = message["channel"]
                incident_id = int(channel.removeprefix(INCIDENT_CHANNEL_PREFIX))
                data = json.loads(message["data"])
                await manager.broadcast_local(incident_id, data)
            except (ValueError, TypeError, json.JSONDecodeError):
                logger.exception("Failed to process pub/sub message: %s", message)
    except asyncio.CancelledError:
        await pubsub.punsubscribe(f"{INCIDENT_CHANNEL_PREFIX}*")
        raise
