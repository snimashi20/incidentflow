from httpx import AsyncClient


async def _create_incident(client: AsyncClient, headers: dict) -> int:
    resp = await client.post(
        "/api/incidents", json={"title": "API down", "description": "", "severity": "HIGH"}, headers=headers
    )
    return resp.json()["id"]


async def test_create_task(client: AsyncClient, register_and_login):
    headers = await register_and_login("taskowner@example.com", "Task Owner")
    incident_id = await _create_incident(client, headers)

    resp = await client.post(
        f"/api/incidents/{incident_id}/tasks", json={"title": "Check API logs"}, headers=headers
    )
    assert resp.status_code == 201
    body = resp.json()
    assert body["title"] == "Check API logs"
    assert body["status"] == "TODO"
    assert body["incident_id"] == incident_id


async def test_create_task_for_missing_incident_404(client: AsyncClient, register_and_login):
    headers = await register_and_login("nope@example.com", "Nope")
    resp = await client.post("/api/incidents/999999/tasks", json={"title": "X"}, headers=headers)
    assert resp.status_code == 404


async def test_update_task_status(client: AsyncClient, register_and_login):
    headers = await register_and_login("updater@example.com", "Updater")
    incident_id = await _create_incident(client, headers)
    create_resp = await client.post(
        f"/api/incidents/{incident_id}/tasks", json={"title": "Restart service"}, headers=headers
    )
    task_id = create_resp.json()["id"]

    resp = await client.patch(f"/api/tasks/{task_id}", json={"status": "IN_PROGRESS"}, headers=headers)
    assert resp.status_code == 200
    assert resp.json()["status"] == "IN_PROGRESS"


async def test_assign_task_to_user(client: AsyncClient, register_and_login):
    owner_headers = await register_and_login("assigner@example.com", "Assigner")
    assignee_headers = await register_and_login("assignee@example.com", "Assignee")

    incident_id = await _create_incident(client, owner_headers)
    create_resp = await client.post(
        f"/api/incidents/{incident_id}/tasks", json={"title": "Contact provider"}, headers=owner_headers
    )
    task_id = create_resp.json()["id"]

    users_resp = await client.get("/api/users", headers=owner_headers)
    assignee_id = next(u["id"] for u in users_resp.json() if u["email"] == "assignee@example.com")

    resp = await client.patch(
        f"/api/tasks/{task_id}", json={"assigned_to": assignee_id}, headers=assignee_headers
    )
    assert resp.status_code == 200
    assert resp.json()["assigned_to"] == assignee_id
