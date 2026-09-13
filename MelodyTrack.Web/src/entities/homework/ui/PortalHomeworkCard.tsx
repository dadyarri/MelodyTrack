import type { ReactNode } from "react";

import { RpgBadge, RpgCard } from "@/shared/ui/rpg";

export function PortalHomeworkCard({ title, description, status, complete = false, action }: {
  title: string;
  description: string;
  status: string;
  complete?: boolean;
  action?: ReactNode;
}) {
  return (
    <RpgCard title={title} description={description} eyebrow="Свиток практики" icon="quill" accent="copper" footer={action}>
      <RpgBadge tone={complete ? "success" : "neutral"}>{status}</RpgBadge>
    </RpgCard>
  );
}
