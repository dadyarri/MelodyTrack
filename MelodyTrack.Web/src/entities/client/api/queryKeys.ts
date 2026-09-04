function optionalKey(value?: string | number | null) {
  return value ?? null;
}

export const clientQueryKeys = {
  all: ["clients"] as const,
  reference: ["clients", "reference"] as const,
  list: (page: number, search: string) => ["clients", "list", page, search] as const,
  history: (clientId?: string | null, page?: number | null, pageSize?: number | null) =>
    ["clients", "history", optionalKey(clientId), optionalKey(page), optionalKey(pageSize)] as const,
  debtors: ["clients", "debtors"] as const,
  lookup: (search: string) => ["clients", "lookup", search] as const,
  selected: (clientId?: string) => ["clients", "selected", optionalKey(clientId)] as const,
  sources: ["client-sources"] as const,
};
