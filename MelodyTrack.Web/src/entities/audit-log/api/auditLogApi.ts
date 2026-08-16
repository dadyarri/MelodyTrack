import { http, type PaginatedParams, type PaginatedResponse } from "@/shared/api";

import type { AuditLog } from "../model/types";

export const auditLogApi = {
  list(params: PaginatedParams & { search?: string; timezone?: string }) {
    return http.get<PaginatedResponse<AuditLog>>("/audit-logs", { params }).then((response) => response.data);
  },
};
