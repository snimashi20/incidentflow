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
  return api.get<User>("/api/auth/me");
}
