import type { PaginatedResponse, RecordActivity, RequiredApiContract } from "@/shared/api";
import type {
  CreatePaymentRequest,
  GetPaymentsClientDto,
  GetPaymentsDto,
  GetPaymentsResponse,
  GetPaymentsServiceDto,
  MoneyListSummaryDto,
} from "@/shared/api/generated/models";

export type PaymentClient = RequiredApiContract<GetPaymentsClientDto, "id" | "firstName" | "lastName">;

export type PaymentService = RequiredApiContract<GetPaymentsServiceDto, "id" | "name">;

export type Payment = Omit<
  RequiredApiContract<GetPaymentsDto, "id" | "client" | "amount" | "date">,
  "client" | "service" | "lastActivity"
> & {
  client: PaymentClient;
  service?: PaymentService | null;
  lastActivity?: RecordActivity | null;
};

export type PaymentInput = RequiredApiContract<CreatePaymentRequest, "clientId" | "amount" | "date">;

type PaymentSummary = RequiredApiContract<MoneyListSummaryDto, "totalAmount" | "itemsCount">;
export type PaymentsResponse = Omit<RequiredApiContract<GetPaymentsResponse, "items" | "page" | "summary">, "items" | "page" | "summary"> &
  PaginatedResponse<Payment> & { summary: PaymentSummary };
