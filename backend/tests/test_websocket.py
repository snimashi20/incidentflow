import pytest
from starlette.testclient import TestClient
from starlette.websockets import WebSocketDisconnect

from app.main import app


def _register_and_login(tc: TestClient, email: str, name: str) -> str:
    tc.post("/api/auth/register", json={"email": email, "name": name, "password": "supersecret123"})
    resp = tc.post("/api/auth/login", json={"email": email, "password": "supersecret123"})
    return resp.json()["access_token"]


def test_websocket_receives_incident_broadcasts():
    with TestClient(app) as tc:
        token = _register_and_login(tc, "wsuser@example.com", "WS User")
        headers = {"Authorization": f"Bearer {token}"}

        create_resp = tc.post(
            "/api/incidents",
            json={"title": "Realtime test", "description": "", "severity": "HIGH"},
            headers=headers,
        )
        incident_id = create_resp.json()["id"]

        with tc.websocket_connect(f"/ws/incidents/{incident_id}?token={token}") as ws:
            tc.patch(
                f"/api/incidents/{incident_id}/status",
                json={"status": "INVESTIGATING"},
                headers=headers,
            )

            first = ws.receive_json()
            second = ws.receive_json()

        assert {first["type"], second["type"]} == {"incident_updated", "activity_created"}
        updated = first if first["type"] == "incident_updated" else second
        assert updated["data"]["status"] == "INVESTIGATING"


def test_websocket_broadcasts_only_to_matching_incident():
    with TestClient(app) as tc:
        token = _register_and_login(tc, "wsscope@example.com", "WS Scope")
        headers = {"Authorization": f"Bearer {token}"}

        incident_a = tc.post(
            "/api/incidents", json={"title": "A", "description": "", "severity": "LOW"}, headers=headers
        ).json()["id"]
        incident_b = tc.post(
            "/api/incidents", json={"title": "B", "description": "", "severity": "LOW"}, headers=headers
        ).json()["id"]

        with tc.websocket_connect(f"/ws/incidents/{incident_a}?token={token}") as ws_a:
            tc.patch(f"/api/incidents/{incident_b}/status", json={"status": "RESOLVED"}, headers=headers)
            tc.patch(f"/api/incidents/{incident_a}/status", json={"status": "RESOLVED"}, headers=headers)

            first = ws_a.receive_json()
            assert first["data"]["id"] == incident_a


def test_websocket_rejects_missing_token():
    with TestClient(app) as tc:
        with pytest.raises(WebSocketDisconnect):
            with tc.websocket_connect("/ws/incidents/1") as ws:
                ws.receive_text()
