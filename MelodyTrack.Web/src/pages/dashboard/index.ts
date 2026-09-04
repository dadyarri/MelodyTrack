import type { QueryClient } from "@tanstack/react-query";

import { analyticsQueryKeys, dashboardApi } from "@/entities/dashboard";

export { DashboardPage } from "./ui/DashboardPage";

export async function prefetchRouteData(queryClient: QueryClient) {
  const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone;

  await queryClient.prefetchQuery({
    queryKey: analyticsQueryKeys.stats(timezone),
    queryFn: () => dashboardApi.stats(timezone),
  });
}
