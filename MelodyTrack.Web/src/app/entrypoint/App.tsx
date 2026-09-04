import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { App as AntdApp } from "antd";

import { AppRouter } from "@/app/router";
import { AuthProvider, authStore } from "@/entities/session";
import { defaultQueryStaleTimeMs } from "@/shared/lib/refetch";
import { configureDraftOwner } from "@/shared/lib/storage";
import { ApiErrorNotifier } from "@/shared/ui";

import { useMobileOverlayKeyboardPolicy } from "../lib/useMobileOverlayKeyboardPolicy";
import { useVisualViewportCssVariables } from "../lib/useVisualViewportCssVariables";

configureDraftOwner(() => authStore.getUserId());

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: defaultQueryStaleTimeMs,
      refetchOnWindowFocus: true,
      refetchIntervalInBackground: false,
      retry: 1,
    },
  },
});

export function App() {
  useMobileOverlayKeyboardPolicy();
  useVisualViewportCssVariables();

  return (
    <QueryClientProvider client={queryClient}>
      <AntdApp>
        <ApiErrorNotifier />
        <AuthProvider>
          <AppRouter />
        </AuthProvider>
      </AntdApp>
    </QueryClientProvider>
  );
}
