import { useQuery } from "@tanstack/react-query";

import { useAuth } from "@/entities/session";

import { clientPortalApi } from "../api/clientPortalApi";
import { clientPortalQueryKeys } from "../api/queryKeys";

export function usePortalEnrollments() {
  const { user } = useAuth();
  return useQuery({
    queryKey: clientPortalQueryKeys.enrollments(user?.linkedClientId),
    queryFn: ({ signal }) => clientPortalApi.courseEnrollments(signal),
    enabled: Boolean(user?.linkedClientId),
  });
}
