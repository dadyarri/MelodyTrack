import type { PaginatedResponse, RecordActivity, RequiredApiContract } from "@/shared/api";
import type { CreateExpenseRequest, ExpenseDto, GetExpensesResponse, MoneyListSummaryDto } from "@/shared/api/generated/models";

export type Expense = Omit<RequiredApiContract<ExpenseDto, "id" | "description" | "amount" | "date">, "lastActivity"> & {
  lastActivity?: RecordActivity | null;
};

export type ExpenseInput = RequiredApiContract<CreateExpenseRequest, "description" | "amount" | "date">;

type ExpenseSummary = RequiredApiContract<MoneyListSummaryDto, "totalAmount" | "itemsCount">;
export type ExpensesResponse = Omit<RequiredApiContract<GetExpensesResponse, "items" | "page" | "summary">, "items" | "page" | "summary"> &
  PaginatedResponse<Expense> & { summary: ExpenseSummary };
