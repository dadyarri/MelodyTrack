import { createContext } from "react";

import type { MeResponse } from "../api/sessionApi";

interface LoginInput {
  email: string;
  password: string;
  otp?: string;
  recoveryCode?: string;
}

export interface AuthContextValue {
  isLoading: boolean;
  isAuthenticated: boolean;
  user: MeResponse | null;
  login: (input: LoginInput) => Promise<MeResponse>;
  establishSession: (accessToken: string) => Promise<MeResponse>;
  logout: () => Promise<void>;
}

export const AuthContext = createContext<AuthContextValue | null>(null);
