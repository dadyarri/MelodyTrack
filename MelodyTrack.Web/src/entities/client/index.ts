export {
  formatClientName,
  getClientContactValue,
  renderClientHistoryAppointmentStatus,
  renderClientPhoneLink,
  renderClientSocialLink,
} from "./lib/client";
export {
  formatPhone,
  formatPhoneInput,
  getPhoneDigits,
  getPhoneUri,
  getSocialHandle,
  getSocialLinkHref,
  hasPhoneDigits,
  isValidPhone,
  normalizePhone,
  normalizeSocialLink,
} from "./lib/contact";
export type {
  Client,
  ClientCalendarSubscription,
  ClientContacts,
  ClientFinancialHistoryEvent,
  ClientFinancialHistoryEventType,
  ClientHistory,
  ClientHistoryAppointmentStatus,
  ClientHistorySummary,
  ClientLifecycleStatus,
  ClientVacation,
  ClientWithBalance,
  CreateClientInput,
  GetClientHistoryParams,
  ListClientsParams,
  LookupClient,
  UpdateClientInput,
} from "./model/types";
export { clientsApi } from "./api/clientApi";
export { clientQueryKeys } from "./api/queryKeys";
export { ClientSelect } from "./ui/ClientSelect";
