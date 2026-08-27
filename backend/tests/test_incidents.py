from httpx import AsyncClient


async def test_create_incident(client: AsyncClient, register_and_login):
    headers = await register_and_login("owner@example.com", "Owner")
    resp = await client.post(
        "/api/incidents",
        json={"title": "Payment API Failure", "description": "500s on checkout", "severity": "HIGH"},
        headers=headers,
    )
    assert resp.status_code == 201
    body = resp.json()
    assert body["title"] == "Payment API Failure"
    assert body["status"] == "OPEN"
    assert len(body["participants"]) == 1
    assert body["participants"][0]["email"] == "owner@example.com"
    assert any("created the incident" in log["message"] for log in body["activity_logs"])


async def test_create_incident_requires_auth(client: AsyncClient):
    resp = await client.post(
        "/api/incidents", json={"title": "X", "description": "", "severity": "LOW"}
    )
    assert resp.status_code == 401


async def test_list_incidents(client: AsyncClient, register_and_login):
    headers = await register_and_login("lister@example.com", "Lister")
    await client.post(
        "/api/incidents", json={"title": "Incident A", "description": "", "severity": "LOW"}, headers=headers
    )
    await client.post(
        "/api/incidents", json={"title": "Incident B", "description": "", "severity": "MEDIUM"}, headers=headers
    )
    resp = await client.get("/api/incidents", headers=headers)
    assert resp.status_code == 200
    titles = {i["title"] for i in resp.json()}
    assert {"Incident A", "Incident B"} <= titles


async def test_update_status_logs_activity(client: AsyncClient, register_and_login):
    headers = await register_and_login("responder@example.com", "Responder")
    create_resp = await client.post(
        "/api/incidents",
        json={"title": "DB outage", "description": "", "severity": "CRITICAL"},
        headers=headers,
    )
    incident_id = create_resp.json()["id"]

    resp = await client.patch(
        f"/api/incidents/{incident_id}/status", json={"status": "INVESTIGATING"}, headers=headers
    )
    assert resp.status_code == 200
    body = resp.json()
    assert body["status"] == "INVESTIGATING"
    assert any("OPEN → INVESTIGATING" in log["message"] for log in body["activity_logs"])


async def test_join_incident_adds_participant_once(client: AsyncClient, register_and_login):
    owner_headers = await register_and_login("creator@example.com", "Creator")
    joiner_headers = await register_and_login("joiner@example.com", "Joiner")

    create_resp = await client.post(
        "/api/incidents", json={"title": "Cache down", "description": "", "severity": "MEDIUM"},
        headers=owner_headers,
    )
    incident_id = create_resp.json()["id"]

    resp1 = await client.post(f"/api/incidents/{incident_id}/join", headers=joiner_headers)
    resp2 = await client.post(f"/api/incidents/{incident_id}/join", headers=joiner_headers)

    assert resp1.status_code == 200
    assert resp2.status_code == 200
    participant_emails = [p["email"] for p in resp2.json()["participants"]]
    assert participant_emails.count("joiner@example.com") == 1


async def test_get_missing_incident_404(client: AsyncClient, register_and_login):
    headers = await register_and_login("ghost@example.com", "Ghost")
    resp = await client.get("/api/incidents/999999", headers=headers)
    assert resp.status_code == 404
