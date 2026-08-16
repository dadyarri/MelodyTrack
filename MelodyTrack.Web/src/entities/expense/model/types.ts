import type { PaginatedResponse, RecordActivity, Ulid } from "@/shared/api";

export interface Expense {
  id: Ulid;
  description: string;
  amount: number;
  date: string;
  categoryId?: Ulid | null;
  categoryName?: string | null;
  lastActivity?: RecordActivity | null;
}

export interface ExpenseInput {
  description: string;
  amount: number;
  date: string;
  categoryId?: Ulid;
}

export interface ExpensesResponse extends PaginatedResponse<Expense> {
  summary: {
    totalAmount: number;
    itemsCount: number;
    firstItemAtUtc?: string | null;
    lastItemAtUtc?: string | null;
  };
}
