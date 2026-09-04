import { keepPreviousData, useQuery } from "@tanstack/react-query";

import { auditLogApi, auditLogQueryKeys } from "@/entities/audit-log";
import { hasSuperuserAccess, useAuth } from "@/entities/session";
import { readPositiveInteger, useUrlState } from "@/shared/lib/react";

export function useAuditPageController() {
  const { searchParams, setUrlState } = useUrlState();
  const auth = useAuth();
  const page = readPositiveInteger(searchParams.get("page"));
  const search = searchParams.get("q") ?? "";
  const timezone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const canViewAudit = hasSuperuserAccess(auth.user);
  const query = useQuery({
    queryKey: auditLogQueryKeys.list(page, search, timezone),
    queryFn: () => auditLogApi.list({ page, page_size: 20, search: search.trim() || undefined, timezone }),
    enabled: canViewAudit,
    placeholderData: keepPreviousData,
  });

  return {
    canViewAudit,
    page,
    search,
    query,
    setPage: (nextPage: number) => {
      setUrlState({ page: nextPage === 1 ? null : nextPage });
    },
    handleSearch: (value: string) => {
      setUrlState({ page: null, q: value.trim() || null });
    },
  };
}
