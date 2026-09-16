// Thin wrappers around the /api/auth/* backend endpoints. Keeping these as
// plain functions (rather than calling `api.post(...)` directly from
// components) means every place that needs to log in calls the same typed
// function, instead of repeating the URL string and payload shape everywhere.
import { api } from "./client";
import type { User } from "../types";

export interface Token {
  access_token: string;
  token_type: string;
}

export function register(email: string, name: string, password: string): Promise<User> {
  return api.post<User>("/api/auth/register", { email, name, password });
}

export function login(email: string, password: string): Promise<Token> {
  return api.post<Token>("/api/auth/login", { email, password });
}

export function me(): Promise<User> {
  // Asks the backend "who does this JWT belong to?" - used to restore a
  // logged-in session after a page refresh (see AuthContext.tsx).
  return api.get<User>("/api/auth/me");
}
