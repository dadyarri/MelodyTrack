import { useQuery } from "@tanstack/react-query";
import { Select } from "antd";
import type { DefaultOptionType } from "antd/es/select";
import { useEffect, useEffectEvent, useMemo } from "react";

import { getCachedReferenceLabel, type ReferenceLabelKind, rememberReferenceLabels } from "@/shared/lib";

import { referenceBookQueryKeys } from "../api/queryKeys";
import { clientSourcesApi, expenseCategoriesApi } from "../api/referenceBookApi";

function ReferenceBookSelect({
  kind,
  queryKey,
  queryFn,
  value,
  onChange,
  extraOptions,
  onResolvedLabelChange,
}: {
  kind: ReferenceLabelKind;
  queryKey: readonly unknown[];
  queryFn: () => Promise<Array<{ id: string; name: string }>>;
  value?: string;
  onChange?: (value: string) => void;
  extraOptions?: DefaultOptionType[];
  onResolvedLabelChange?: (label?: string) => void;
}) {
  const query = useQuery({ queryKey, queryFn, retry: false });
  const cachedLabel = getCachedReferenceLabel(kind, value);
  const notifyResolvedLabel = useEffectEvent((label?: string) => {
    onResolvedLabelChange?.(label);
  });
  const options = useMemo<DefaultOptionType[]>(() => {
    const selectedOption = cachedLabel && value ? [{ value, label: cachedLabel }] : [];
    const lookupOptions = query.data?.map((item) => ({ value: item.id, label: item.name })) ?? [];
    return [...selectedOption, ...(extraOptions ?? []), ...lookupOptions].filter(
      (option, index, items) => items.findIndex((item) => item.value === option.value) === index,
    );
  }, [cachedLabel, extraOptions, query.data, value]);
  const selectedLabel = options.find((option) => option.value === value)?.label ?? cachedLabel;
  const resolvedLabel = typeof selectedLabel === "string" ? selectedLabel : undefined;

  useEffect(() => {
    notifyResolvedLabel(resolvedLabel);
  }, [resolvedLabel]);

  useEffect(() => {
    if (query.data) {
      rememberReferenceLabels(
        kind,
        query.data.map((item) => ({ id: item.id, label: item.name })),
      );
    }
  }, [kind, query.data]);

  return <Select className="wide" allowClear loading={query.isLoading} options={options} value={value} onChange={onChange} />;
}

type SelectProps = {
  value?: string;
  onChange?: (value: string) => void;
  extraOptions?: DefaultOptionType[];
  onResolvedLabelChange?: (label?: string) => void;
};

export function ExpenseCategorySelect(props: SelectProps) {
  return (
    <ReferenceBookSelect
      {...props}
      kind="expense-category"
      queryKey={referenceBookQueryKeys.expenseCategories}
      queryFn={() => expenseCategoriesApi.list()}
    />
  );
}

export function ClientSourceSelect(props: SelectProps) {
  return (
    <ReferenceBookSelect
      {...props}
      kind="client-source"
      queryKey={referenceBookQueryKeys.clientSources}
      queryFn={() => clientSourcesApi.list()}
    />
  );
}
