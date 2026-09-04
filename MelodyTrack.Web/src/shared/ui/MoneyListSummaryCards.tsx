import type { ReactNode } from "react";

import { formatMoney } from "@/shared/lib";
import { SummaryCard, SummaryGrid } from "@/shared/ui";

export function MoneyListSummaryCards({
  totalAmount,
  itemsCount,
  lastItemAtLabel,
  itemsTitle,
  lastItemTitle,
}: {
  totalAmount?: number | null;
  itemsCount?: number | null;
  lastItemAtLabel: string;
  itemsTitle: ReactNode;
  lastItemTitle: ReactNode;
}) {
  return (
    <SummaryGrid>
      <SummaryCard title="Сумма по выборке" value={formatMoney(totalAmount)} />
      <SummaryCard title={itemsTitle} value={itemsCount ?? 0} />
      <SummaryCard title={lastItemTitle} value={lastItemAtLabel} />
    </SummaryGrid>
  );
}
