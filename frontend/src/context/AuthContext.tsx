// React Context is a way to share a value (here: "who is logged in, and how
// do I log in/out") with any component in the tree, without manually passing
// it down as a prop through every layer in between ("prop drilling"). Any
// component can call useAuth() below to read the current user or trigger login/logout.
import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import * as authApi from "../api/auth";
import { clearToken, getToken, setToken } from "../api/client";
import type { User } from "../types";

interface AuthContextValue {
  user: User | null;
  loading: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (input: authApi.RegisterInput) => Promise<void>;
  logout: () => void;
}

// The Context object itself - starts as `undefined` until a Provider wraps
// the tree and supplies a real value (see AuthProvider below).
const AuthContext = createContext<AuthContextValue | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  // Starts true so the app can show a "Loading..." state instead of
  // flashing the login page for a moment before we've checked for a saved token.
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    // Runs once when the app first mounts: if we have a saved token from a
    // previous session, ask the backend who it belongs to and restore the
    // logged-in state - this is what keeps you logged in across page refreshes.
    if (!getToken()) {
      setLoading(false);
      return;
    }
    authApi
      .me()
      .then(setUser)
      .catch(() => clearToken()) // token was invalid/expired - discard it
      .finally(() => setLoading(false));
  }, []);

  async function login(email: string, password: string) {
    const token = await authApi.login(email, password);
    setToken(token.access_token);
    setUser(await authApi.me());
  }

  async function register(input: authApi.RegisterInput) {
    await authApi.register(input);
    // Registering doesn't log you in by itself - immediately follow up with
    // a normal login so the new user lands straight in the app.
    await login(input.email, input.password);
  }

  function logout() {
    clearToken();
    setUser(null);
  }

  return (
    <AuthContext.Provider value={{ user, loading, login, register, logout }}>{children}</AuthContext.Provider>
  );
}

// The hook every component actually uses: `const { user, logout } = useAuth();`
export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
