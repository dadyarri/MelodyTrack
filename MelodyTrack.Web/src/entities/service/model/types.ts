import type { RecordActivity, Ulid } from "@/shared/api";

export interface Service {
  id: Ulid;
  name: string;
  publicName?: string | null;
  description?: string | null;
  isConsultation: boolean;
  price: number;
  lastActivity?: RecordActivity | null;
}

export interface LookupService {
  id: Ulid;
  name: string;
  price?: number;
}

export interface ServiceInput {
  name: string;
  publicName?: string;
  description?: string;
  isConsultation: boolean;
}
