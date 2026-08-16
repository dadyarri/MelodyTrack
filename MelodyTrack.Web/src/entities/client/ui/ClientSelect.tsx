import { useQuery } from "@tanstack/react-query";
import { Select } from "antd";
import type { DefaultOptionType } from "antd/es/select";
import { useEffect, useEffectEvent, useMemo, useState } from "react";

import { getCachedReferenceLabel, rememberReferenceLabel, rememberReferenceLabels, useDebouncedValue } from "@/shared/lib";

import { clientsApi } from "../api/clientApi";
import { clientQueryKeys } from "../api/queryKeys";

function formatClientLabel(client: { firstName: string; lastName: string; patronymic?: string | null }) {
  return [client.lastName, client.firstName, client.patronymic].filter(Boolean).join(" ");
}

export function ClientSelect({
  value,
  onChange,
  extraOptions,
  onResolvedLabelChange,
}: {
  value?: string;
  onChange?: (value: string) => void;
  extraOptions?: DefaultOptionType[];
  onResolvedLabelChange?: (label?: string) => void;
}) {
  const [search, setSearch] = useState("");
  const debouncedSearch = useDebouncedValue(search.trim(), 250);
  const query = useQuery({
    queryKey: clientQueryKeys.lookup(debouncedSearch),
    queryFn: ({ signal }) => clientsApi.lookup(debouncedSearch, signal),
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
  const selectedQuery = useQuery({
    queryKey: clientQueryKeys.selected(value),
    queryFn: () => {
      if (!value) {
        throw new Error("Client id is missing.");
      }
      return clientsApi.get(value);
    },
    enabled: Boolean(value),
    retry: false,
    staleTime: 5 * 60 * 1000,
  });
  const cachedLabel = getCachedReferenceLabel("client", value);
  const notifyResolvedLabel = useEffectEvent((label?: string) => {
    onResolvedLabelChange?.(label);
  });
  const selectedLabel = selectedQuery.data ? formatClientLabel(selectedQuery.data) : cachedLabel;
  const options = useMemo<DefaultOptionType[]>(() => {
    const selectedOption = selectedQuery.data
      ? [{ value: selectedQuery.data.id, label: formatClientLabel(selectedQuery.data) }]
      : cachedLabel
        ? [{ value, label: cachedLabel }]
        : [];
    const lookupOptions = query.data?.map((client) => ({ value: client.id, label: formatClientLabel(client) })) ?? [];
    const mergedOptions = [...selectedOption, ...(extraOptions ?? []), ...lookupOptions];

    return mergedOptions.filter((option, index, items) => items.findIndex((item) => item.value === option.value) === index);
  }, [cachedLabel, extraOptions, query.data, selectedQuery.data, value]);

  useEffect(() => {
    notifyResolvedLabel(selectedLabel);
  }, [selectedLabel]);

  useEffect(() => {
    if (query.data) {
      rememberReferenceLabels(
        "client",
        query.data.map((client) => ({ id: client.id, label: formatClientLabel(client) })),
      );
    }
  }, [query.data]);

  useEffect(() => {
    if (selectedQuery.data) {
      rememberReferenceLabel("client", selectedQuery.data.id, formatClientLabel(selectedQuery.data));
    }
  }, [selectedQuery.data]);

  return (
    <Select
      className="wide"
      showSearch={{ filterOption: false, onSearch: setSearch }}
      allowClear
      loading={query.isLoading || selectedQuery.isLoading}
      options={options}
      placeholder="Начните вводить ФИО"
      value={value}
      onChange={onChange}
    />
  );
}
