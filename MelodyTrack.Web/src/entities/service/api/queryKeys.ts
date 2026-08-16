function optionalKey(value?: string | null) {
  return value ?? null;
}

export const serviceQueryKeys = {
  all: ["services"] as const,
  reference: ["services", "reference"] as const,
  list: (page: number) => ["services", "list", page] as const,
  lookup: (search: string) => ["services", "lookup", search] as const,
  selected: (serviceId?: string) => ["services", "selected", optionalKey(serviceId)] as const,
};
