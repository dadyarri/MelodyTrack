import type { PaginatedResponse, RecordActivity, Ulid } from "@/shared/api";

export interface PaymentClient {
  id: Ulid;
  firstName: string;
  lastName: string;
  patronymic?: string | null;
}

export interface PaymentService {
  id: Ulid;
  name: string;
}

export interface Payment {
  id: Ulid;
  client: PaymentClient;
  service?: PaymentService | null;
  amount: number;
  date: string;
  description?: string | null;
  lastActivity?: RecordActivity | null;
}

export interface PaymentInput {
  clientId: Ulid;
  serviceId?: Ulid;
  amount: number;
  date: string;
  description?: string;
}

export interface PaymentsResponse extends PaginatedResponse<Payment> {
  summary: {
    totalAmount: number;
    itemsCount: number;
    firstItemAtUtc?: string | null;
    lastItemAtUtc?: string | null;
  };
}
