import type { Dayjs } from "dayjs";

function dayjsKey(value?: Dayjs | null) {
  return value?.toISOString() ?? null;
}

export const paymentQueryKeys = {
  all: ["payments"] as const,
  list: (page: number, search: string, clientId?: string, serviceId?: string, startDate?: Dayjs | null, endDate?: Dayjs | null) =>
    ["payments", "list", page, search, clientId ?? null, serviceId ?? null, dayjsKey(startDate), dayjsKey(endDate)] as const,
};
