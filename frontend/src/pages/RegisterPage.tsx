// The registration form - structurally almost identical to LoginPage.tsx,
// just with extra fields (name, department, job title) and calling
// register() instead of login().
//
// Picking a department is mandatory: everyone in the company belongs to
// exactly one, and incidents get routed by department, so an account with
// no department would be unassignable.
import { FormEvent, useEffect, useState } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../context/AuthContext";
import { ApiError } from "../api/client";
import { listDepartments } from "../api/departments";
import { SENIORITY_LABELS, SENIORITY_ORDER } from "../lib/seniority";
import type { Department, DepartmentKind, Seniority } from "../types";

// The three groups, in the order we want them to appear, with the heading
// to show above each one.
const KIND_LABELS: { kind: DepartmentKind; label: string }[] = [
  { kind: "PRODUCT_AREA", label: "Product areas" },
  { kind: "CORPORATE_FUNCTION", label: "Corporate functions" },
  { kind: "CAMPUS_OPERATIONS", label: "Campus operations" },
];

export function RegisterPage() {
  const { register } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [name, setName] = useState("");
  const [password, setPassword] = useState("");
  const [departmentId, setDepartmentId] = useState("");
  const [jobTitle, setJobTitle] = useState("");
  // Defaults to MID, matching what the backend assumes when no level is sent.
  const [seniority, setSeniority] = useState<Seniority>("MID");
  const [departments, setDepartments] = useState<Department[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  // Load the department list once when the page first appears. This
  // endpoint deliberately works without a token - someone registering
  // doesn't have one yet.
  useEffect(() => {
    listDepartments()
      .then(setDepartments)
      .catch(() => setError("Could not load the department list. Is the backend running?"));
  }, []);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setSubmitting(true);
    try {
      await register({
        email,
        name,
        password,
        // The <select> gives us its value as a string, so convert before
        // sending - the backend expects a number.
        departmentId: Number(departmentId),
        jobTitle: jobTitle.trim() || undefined,
        seniority,
      });
      navigate("/incidents");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Registration failed");
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <div className="auth-page">
      <form className="auth-card" onSubmit={handleSubmit}>
        <h1>IncidentFlow</h1>
        <p className="muted">Create your account</p>
        {error && <div className="error-banner">{error}</div>}
        <label>
          Name
          <input value={name} onChange={(e) => setName(e.target.value)} required />
        </label>
        <label>
          Email
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </label>
        <label>
          Department
          <select
            value={departmentId}
            onChange={(e) => setDepartmentId(e.target.value)}
            required // the browser blocks submitting while the empty option is selected
          >
            <option value="">Select your department...</option>
            {/* <optgroup> draws a non-selectable heading above a set of
                options, which keeps 14 departments readable. */}
            {KIND_LABELS.map(({ kind, label }) => {
              const inGroup = departments.filter((d) => d.kind === kind);
              if (inGroup.length === 0) return null;
              return (
                <optgroup key={kind} label={label}>
                  {inGroup.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name}
                    </option>
                  ))}
                </optgroup>
              );
            })}
          </select>
        </label>
        <label>
          Job title <span className="muted">(optional)</span>
          <input
            value={jobTitle}
            onChange={(e) => setJobTitle(e.target.value)}
            placeholder="e.g. Site Reliability Engineer"
          />
        </label>
        <label>
          Level
          {/* Listed most senior first, so executives and directors are at
              the top rather than hidden at the bottom of the list. */}
          <select value={seniority} onChange={(e) => setSeniority(e.target.value as Seniority)}>
            {[...SENIORITY_ORDER].reverse().map((level) => (
              <option key={level} value={level}>
                {SENIORITY_LABELS[level]}
              </option>
            ))}
          </select>
        </label>
        <label>
          Password
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            minLength={8} // basic client-side check; the backend doesn't currently enforce a minimum length itself
            required
          />
        </label>
        <button type="submit" disabled={submitting}>
          {submitting ? "Creating account..." : "Register"}
        </button>
        <p className="muted">
          Already have an account? <Link to="/login">Sign in</Link>
        </p>
      </form>
    </div>
  );
}
