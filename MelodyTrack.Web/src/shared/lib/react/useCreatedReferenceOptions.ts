import type { DefaultOptionType } from "antd/es/select";
import { useCallback, useState } from "react";

import { type ReferenceLabelKind, rememberReferenceLabel } from "@/shared/lib";

type AddCreatedReferenceOptionInput = {
  id: string;
  label: string;
  optionLabel?: string;
};

export function useCreatedReferenceOptions(kind?: ReferenceLabelKind) {
  const [createdOptions, setCreatedOptions] = useState<DefaultOptionType[]>([]);

  const addCreatedOption = useCallback(
    ({ id, label, optionLabel }: AddCreatedReferenceOptionInput) => {
      const nextOption = { value: id, label: optionLabel ?? label } satisfies DefaultOptionType;
      setCreatedOptions((current) => [nextOption, ...current.filter((item) => item.value !== nextOption.value)]);

      if (kind) {
        rememberReferenceLabel(kind, id, label);
      }

      return nextOption;
    },
    [kind],
  );

  return {
    createdOptions,
    addCreatedOption,
    clearCreatedOptions: useCallback(() => {
      setCreatedOptions([]);
    }, []),
  };
}
