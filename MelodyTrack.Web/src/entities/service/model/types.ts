import type { RecordActivity, RequiredApiContract } from "@/shared/api";
import type { CreateServiceRequest, LookupServicesDto, ServiceWithCurrentPriceDto } from "@/shared/api/generated/models";

export type Service = Omit<RequiredApiContract<ServiceWithCurrentPriceDto, "id" | "name" | "isConsultation" | "price">, "lastActivity"> & {
  lastActivity?: RecordActivity | null;
};

export type LookupService = RequiredApiContract<LookupServicesDto, "id" | "name" | "price">;

export type ServiceInput = Omit<RequiredApiContract<CreateServiceRequest, "name" | "isConsultation">, "price">;
