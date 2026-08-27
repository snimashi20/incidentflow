from fastapi import APIRouter, Depends, Query, WebSocket, WebSocketDisconnect
from sqlalchemy.ext.asyncio import AsyncSession

from app.database import get_db
from app.deps import get_current_user_ws
from app.ws_manager import manager

router = APIRouter(tags=["websocket"])


@router.websocket("/ws/incidents/{incident_id}")
async def incident_socket(
    websocket: WebSocket,
    incident_id: int,
    token: str | None = Query(default=None),
    db: AsyncSession = Depends(get_db),
) -> None:
    user = await get_current_user_ws(token, db)
    if user is None:
        await websocket.close(code=4401)
        return

    await manager.connect(incident_id, websocket)
    try:
        while True:
            # Clients only receive broadcasts; any inbound message is ignored,
            # but we still need to await recv() to detect disconnects.
            await websocket.receive_text()
    except WebSocketDisconnect:
        pass
    finally:
        manager.disconnect(incident_id, websocket)
