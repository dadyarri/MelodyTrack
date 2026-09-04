import { Space } from "antd";
import type { ReactNode } from "react";

type ListPageScaffoldProps = {
  contentOnboardingId?: string;
  filtersOnboardingId?: string;
  summaryOnboardingId?: string;
  filters?: ReactNode;
  summary?: ReactNode;
  table: ReactNode;
};

export function ListPageScaffold({
  contentOnboardingId,
  filtersOnboardingId,
  summaryOnboardingId,
  filters,
  summary,
  table,
}: ListPageScaffoldProps) {
  return (
    <div data-onboarding-id={contentOnboardingId}>
      {filters ? <div data-onboarding-id={filtersOnboardingId}>{filters}</div> : null}
      {summary ? (
        <Space orientation="vertical" size={20} className="wide" data-onboarding-id={summaryOnboardingId}>
          {summary}
          {table}
        </Space>
      ) : (
        table
      )}
    </div>
  );
}
