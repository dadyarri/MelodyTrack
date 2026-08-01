import { useQuery, useQueryClient } from "@tanstack/react-query";
import axios from "axios";
import { type ReactNode, useCallback, useEffect, useMemo, useState } from "react";

import { authExpiredEventName, configureHttpSession, http, restoreAccessToken } from "@/shared/api";
import { clearReferenceLabels } from "@/shared/lib";

import { authQueryKeys } from "../api/queryKeys";
import { authApi, type MeResponse } from "../api/sessionApi";
import { AuthContext, type AuthContextValue } from "./AuthContext";
import { authStore } from "./authStore";
import { logoutSession } from "./logoutSession";

configureHttpSession(authStore);

class SessionRestoreError extends Error {}

async function loadCurrentUser() {
  if (!authStore.getAccessToken() && !(await restoreAccessToken())) {
    throw new SessionRestoreError("Не удалось восстановить сессию.");
  }

  const me = await authApi.getMe();
  authStore.setUserId(me.id);
  return me;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient();
  const [hasSession, setHasSession] = useState(() => authStore.hasSession());
  const [cachedUser, setCachedUser] = useState<MeResponse | null>(null);

  const clearSessionQueries = useCallback(() => {
    clearReferenceLabels();
    setCachedUser(null);
    void queryClient.cancelQueries({ queryKey: authQueryKeys.me });
    void queryClient.cancelQueries({ queryKey: authQueryKeys.sessions });
    queryClient.removeQueries({ queryKey: authQueryKeys.me });
    queryClient.removeQueries({ queryKey: authQueryKeys.sessions });
    queryClient.removeQueries({ queryKey: ["users", "availability", null] });
    queryClient.removeQueries({ queryKey: ["client-portal"] });
  }, [queryClient]);

  const handleSessionExpired = useCallback(() => {
    authStore.clear();
  }, []);

  const meQuery = useQuery({
    queryKey: authQueryKeys.me,
    queryFn: loadCurrentUser,
    enabled: hasSession,
    retry: false,
    gcTime: 0,
  });

  useEffect(() => {
    window.addEventListener(authExpiredEventName, handleSessionExpired);
    return () => {
      window.removeEventListener(authExpiredEventName, handleSessionExpired);
    };
  }, [handleSessionExpired]);

  useEffect(
    () =>
      authStore.subscribe(({ hasSession: sessionAvailable, source }) => {
        setHasSession(sessionAvailable);
        if (sessionAvailable && source === "external") {
          void queryClient.invalidateQueries({ queryKey: authQueryKeys.me });
        } else {
          clearSessionQueries();
        }
      }),
    [clearSessionQueries, queryClient],
  );

  useEffect(() => {
    if (!hasSession || !meQuery.isError) {
      return;
    }

    if (meQuery.error instanceof SessionRestoreError || (axios.isAxiosError(meQuery.error) && meQuery.error.response?.status === 401)) {
      const timeoutId = window.setTimeout(() => {
        handleSessionExpired();
      }, 0);
      return () => {
        window.clearTimeout(timeoutId);
      };
    }
  }, [hasSession, handleSessionExpired, meQuery.error, meQuery.isError]);

  const loadMe = useCallback(
    async (accessToken: string) => {
      authStore.setSession(accessToken);
      setHasSession(true);
      const me = await queryClient.fetchQuery<MeResponse>({
        queryKey: authQueryKeys.me,
        queryFn: loadCurrentUser,
        staleTime: 0,
        gcTime: 0,
      });
      setCachedUser(me);
      return me;
    },
    [queryClient],
  );

  const value = useMemo<AuthContextValue>(
    () => ({
      isLoading: hasSession && meQuery.isPending,
      isAuthenticated: hasSession && Boolean(meQuery.data ?? cachedUser),
      user: meQuery.data ?? cachedUser,
      async login(input) {
        const response = await http.post<{
          accessToken: string;
        }>("/auth/login", input);
        return loadMe(response.data.accessToken);
      },
      async establishSession(accessToken) {
        return loadMe(accessToken);
      },
      async logout() {
        await logoutSession({
          revoke: () => http.post("/auth/logout", {}).then(() => undefined),
          clear: () => {
            authStore.clear();
          },
        }).catch(() => undefined);
      },
    }),
    [cachedUser, hasSession, loadMe, meQuery.data, meQuery.isPending],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
