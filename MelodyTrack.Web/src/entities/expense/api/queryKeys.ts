import type { Dayjs } from "dayjs";

function dayjsKey(value?: Dayjs | null) {
  return value?.toISOString() ?? null;
}

export const expenseQueryKeys = {
  all: ["expenses"] as const,
  list: (page: number, search: string, startDate?: Dayjs | null, endDate?: Dayjs | null) =>
    ["expenses", "list", page, search, dayjsKey(startDate), dayjsKey(endDate)] as const,
};
