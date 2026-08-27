from httpx import AsyncClient


async def test_register_creates_user(client: AsyncClient):
    resp = await client.post(
        "/api/auth/register",
        json={"email": "alice@example.com", "name": "Alice", "password": "supersecret123"},
    )
    assert resp.status_code == 201
    body = resp.json()
    assert body["email"] == "alice@example.com"
    assert "hashed_password" not in body


async def test_register_duplicate_email_rejected(client: AsyncClient):
    payload = {"email": "bob@example.com", "name": "Bob", "password": "supersecret123"}
    await client.post("/api/auth/register", json=payload)
    resp = await client.post("/api/auth/register", json=payload)
    assert resp.status_code == 400


async def test_login_success_returns_token(client: AsyncClient):
    await client.post(
        "/api/auth/register",
        json={"email": "carol@example.com", "name": "Carol", "password": "supersecret123"},
    )
    resp = await client.post(
        "/api/auth/login", json={"email": "carol@example.com", "password": "supersecret123"}
    )
    assert resp.status_code == 200
    assert resp.json()["token_type"] == "bearer"
    assert resp.json()["access_token"]


async def test_login_wrong_password_rejected(client: AsyncClient):
    await client.post(
        "/api/auth/register",
        json={"email": "dave@example.com", "name": "Dave", "password": "supersecret123"},
    )
    resp = await client.post(
        "/api/auth/login", json={"email": "dave@example.com", "password": "wrongpassword"}
    )
    assert resp.status_code == 401


async def test_me_requires_authentication(client: AsyncClient):
    resp = await client.get("/api/auth/me")
    assert resp.status_code == 401


async def test_me_returns_current_user(client: AsyncClient, register_and_login):
    headers = await register_and_login("erin@example.com", "Erin")
    resp = await client.get("/api/auth/me", headers=headers)
    assert resp.status_code == 200
    assert resp.json()["email"] == "erin@example.com"
