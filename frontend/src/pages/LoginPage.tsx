// The login form. Notice this component doesn't talk to the API directly -
// it calls `login()` from AuthContext, which handles the actual request and
// updates the shared "who is logged in" state that the rest of the app reads.
import { FormEvent, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { ApiError } from "../api/client";

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate(); // lets us redirect the browser after a successful login
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false); // disables the button while the request is in flight

  async function handleSubmit(e: FormEvent) {
    e.preventDefault(); // stop the browser from doing a full page reload on form submit
    setError(null);
    setSubmitting(true);
    try {
      await login(email, password);
      navigate("/incidents");
    } catch (err) {
      // ApiError carries the backend's actual error message (e.g. "Invalid
      // email or password"); anything else falls back to a generic message.
      setError(err instanceof ApiError ? err.message : "Login failed");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={handleSubmit}>
        <h1>IncidentFlow</h1>
        <p className="muted">Sign in to your account</p>
        {error && <div className="error-banner">{error}</div>}
        <label>
          Email
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </label>
        <label>
          Password
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </label>
        <button type="submit" disabled={submitting}>
          {submitting ? "Signing in..." : "Sign in"}
        </button>
        <p className="muted">
          No account? <Link to="/register">Register</Link>
        </p>
      </form>
    </div>
  );
}
