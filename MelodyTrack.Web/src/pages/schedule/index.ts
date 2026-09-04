import type { QueryClient } from "@tanstack/react-query";
import dayjs from "dayjs";

import { appointmentQueryKeys, appointmentsApi } from "@/entities/appointment";

export { SchedulePage } from "./ui/SchedulePage";

const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone;

export async function prefetchRouteData(queryClient: QueryClient) {
  const weekStart = dayjs().startOf("week");
  const weekEnd = weekStart.endOf("week");

  await queryClient.prefetchQuery({
    queryKey: appointmentQueryKeys.appointments(weekStart.toISOString(), weekEnd.toISOString()),
    queryFn: () => appointmentsApi.list({ timezone, startDate: weekStart.toISOString(), endDate: weekEnd.toISOString() }),
  });
}
