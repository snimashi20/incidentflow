// A small wrapper around the browser's built-in fetch() that every other
// api/*.ts file uses. Its jobs: attach the JWT to every request automatically,
// and turn a non-2xx response into a thrown error instead of an object the
// caller has to remember to check.

// import.meta.env.* values come from Vite - they're baked in at build time
// from .env files / docker-compose environment variables (see VITE_API_URL).
const API_URL = import.meta.env.VITE_API_URL ?? "http://localhost:8000";
const TOKEN_KEY = "incidentflow_token";

// We store the JWT in the browser's localStorage so the user stays logged in
// after refreshing the page or closing/reopening the tab.
export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string): void {
  localStorage.setItem(TOKEN_KEY, token);
}

export function clearToken(): void {
  localStorage.removeItem(TOKEN_KEY);
}

// A custom Error subclass so callers can check `err instanceof ApiError` and
// read the HTTP status code, instead of just getting a generic Error.
export class ApiError extends Error {
  status: number;

  constructor(status: number, message: string) {
    super(message);
    this.status = status;
  }
}

async function request<T>(path: string, options: RequestInit = {}): Promise<T> {
  const token = getToken();
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    ...(options.headers as Record<string, string> | undefined),
  };
  // Attach the JWT on every request, if we have one - this is what the
  // backend's CurrentUser.RequireAsync() reads (see backend/IncidentFlow.Api/Common/CurrentUser.cs).
  if (token) headers.Authorization = `Bearer ${token}`;

  const res = await fetch(`${API_URL}${path}`, { ...options, headers });

  if (!res.ok) {
    // Backend error responses look like {"detail": "some message"} - try to
    // read that, but fall back to the plain HTTP status text if the body
    // isn't JSON (or there's no body at all).
    let detail = res.statusText;
    try {
      const body = await res.json();
      detail = body.detail ?? detail;
    } catch {
      // response had no JSON body
    }
    throw new ApiError(res.status, detail);
  }

  if (res.status === 204) return undefined as T;
  return res.json() as Promise<T>;
}

// A tiny typed helper so call sites read as `api.get<IncidentDetail>(...)`
// instead of repeating `fetch(...).then(r => r.json())` everywhere.
export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "POST", body: body ? JSON.stringify(body) : undefined }),
  patch: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "PATCH", body: body ? JSON.stringify(body) : undefined }),
};

// Builds the WebSocket URL for a given incident, including the JWT as a
// query parameter - browsers can't attach custom headers to a WebSocket
// handshake, so this is the standard workaround (matches routers/ws.py on the backend).
export function wsUrl(path: string): string {
  const base = import.meta.env.VITE_WS_URL ?? "ws://localhost:8000";
  const token = getToken();
  return `${base}${path}?token=${encodeURIComponent(token ?? "")}`;
}
