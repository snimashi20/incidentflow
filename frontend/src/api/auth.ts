// Thin wrappers around the /api/auth/* backend endpoints. Keeping these as
// plain functions (rather than calling `api.post(...)` directly from
// components) means every place that needs to log in calls the same typed
// function, instead of repeating the URL string and payload shape everywhere.
import { api } from "./client";
import type { Seniority, User } from "../types";

export interface Token {
  access_token: string;
  token_type: string;
}

// Registration needs five or six things now, so they travel as one object
// rather than a long list of positional arguments - with six `string`
// parameters in a row it's far too easy to swap two of them by accident and
// get no complaint from the compiler.
export interface RegisterInput {
  email: string;
  name: string;
  password: string;
  // Required: the backend refuses to create an employee who doesn't belong
  // to a department.
  departmentId: number;
  jobTitle?: string;
  // Omitted means the backend defaults the new account to MID.
  seniority?: Seniority;
}

export function register(input: RegisterInput): Promise<User> {
  return api.post<User>("/api/auth/register", {
    email: input.email,
    name: input.name,
    password: input.password,
    department_id: input.departmentId,
    job_title: input.jobTitle ?? null,
    seniority: input.seniority ?? null,
  });
}

export function login(email: string, password: string): Promise<Token> {
  return api.post<Token>("/api/auth/login", { email, password });
}

export function me(): Promise<User> {
  // Asks the backend "who does this JWT belong to?" - used to restore a
  // logged-in session after a page refresh (see AuthContext.tsx).
  return api.get<User>("/api/auth/me");
}
