// Defines the app's page routing: which component renders for which URL, and
// which pages require you to be logged in. This is the "React Router" piece
// mentioned in the project brief's tech stack.
import { Navigate, Route, Routes } from "react-router-dom";
import { AuthProvider, useAuth } from "./context/AuthContext";
import { LoginPage } from "./pages/LoginPage";
import { RegisterPage } from "./pages/RegisterPage";
import { IncidentsListPage } from "./pages/IncidentsListPage";
import { IncidentDetailPage } from "./pages/IncidentDetailPage";

/** Wraps a page and redirects to /login if nobody is signed in. Used below
 * for every route except /login and /register themselves. */
function RequireAuth({ children }: { children: React.ReactElement }) {
  const { user, loading } = useAuth();
  if (loading) return <div className="page-body">Loading...</div>;
  if (!user) return <Navigate to="/login" replace />;
  return children;
}

function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/register" element={<RegisterPage />} />
      <Route
        path="/incidents"
        element={
          <RequireAuth>
            <IncidentsListPage />
          </RequireAuth>
        }
      />
      <Route
        path="/incidents/:id"
        element={
          <RequireAuth>
            <IncidentDetailPage />
          </RequireAuth>
        }
      />
      {/* Catch-all: any unknown URL just redirects to the incidents list. */}
      <Route path="*" element={<Navigate to="/incidents" replace />} />
    </Routes>
  );
}

export default function App() {
  return (
    // AuthProvider wraps everything so any page/component below can call
    // useAuth() to read the logged-in user or log in/out.
    <AuthProvider>
      <AppRoutes />
    </AuthProvider>
  );
}
