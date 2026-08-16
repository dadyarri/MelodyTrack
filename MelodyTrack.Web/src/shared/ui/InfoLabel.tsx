import { Tooltip } from "antd";
import type { ReactNode } from "react";

import { InfoCircleOutlined } from "@/shared/ui/icons";

export function InfoLabel({ label, tooltip }: { label: ReactNode; tooltip: ReactNode }) {
  return (
    <span style={{ display: "inline-flex", alignItems: "center", gap: 6 }}>
      <span>{label}</span>
      <Tooltip title={tooltip}>
        <InfoCircleOutlined style={{ color: "var(--text-main)" }} />
      </Tooltip>
    </span>
  );
}
