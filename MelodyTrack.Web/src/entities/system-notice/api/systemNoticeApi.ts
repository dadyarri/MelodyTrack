import { http, type RequiredApiContract, type Ulid } from "@/shared/api";
import type { GetSystemNoticesResponse } from "@/shared/api/generated/models";

import type { SystemNotice } from "../model/types";

type SystemNoticesResponse = Omit<RequiredApiContract<GetSystemNoticesResponse, "items">, "items"> & {
  items: SystemNotice[];
};

export const systemNoticeApi = {
  list(signal?: AbortSignal) {
    return http.get<SystemNoticesResponse>("/system-notices", { signal }).then((response) => response.data.items);
  },
  listPreAuth(signal?: AbortSignal) {
    return http
      .get<SystemNoticesResponse>("/system-notices/pre-auth", { signal, skipAuthRefresh: true })
      .then((response) => response.data.items);
  },
  dismiss(id: Ulid) {
    return http.post<unknown>(`/system-notices/${id}/dismissals`, {}).then(() => undefined);
  },
};
